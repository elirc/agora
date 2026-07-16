using System.Text.RegularExpressions;

namespace Agora.ArchitectureTests;

/// <summary>
/// Bans ambient time reads from the domain — the fitness function for "the clock is a port".
/// </summary>
// 📘 YOU CAN BAN A CATEGORY OF BUG IN CI, and this file is what that looks like.
//
// Every other test in this project asserts something about behaviour. This one asserts something
// about SOURCE TEXT, which normally deserves suspicion — but the rule it enforces cannot be expressed
// any other way. "No code in Agora.Domain may read the wall clock" is not a statement about what the
// domain computes; it is a statement about what the domain is ALLOWED TO ASK FOR. A behavioural test
// can only catch the instances of this bug someone thought to write a test for, and the whole trouble
// with ambient time is that its instances are invisible until a customer in another timezone finds
// one. So we ban the capability instead of chasing the symptoms.
//
// The dependency-rule tests next door do the same thing with references: they don't check that
// Domain behaves as if it has no framework, they check that it CANNOT reach one. Same instinct, one
// layer lower. A rule that can be violated by accident is not a rule; it is a hope with good posture.
//
// TRY: add `var now = DateTime.Now;` to any file under src/Agora.Domain and run this test. It names
// the file and the line. The locations of *your* mistakes are fair game too.
public class AmbientTimeBanTests
{
    // The ambient readers: each one answers "what time is it?" without being asked by anybody who
    // could be held responsible for the answer.
    //
    // ⚠️ DateTime.UtcNow AND DateTimeOffset.UtcNow ARE ON THIS LIST, and that surprises people. UTC
    // is not the problem — timezones are only ever the *symptom*. The problem is that a function
    // which fetches the current instant for itself cannot be asked about any other instant, so it
    // cannot be tested at a moment of your choosing, and it will disagree with the injected clock
    // beside it the first time the two are read a millisecond apart (or a test freezes one of them).
    // A domain that reads UtcNow directly is untestable in exactly the way the campaign bug was
    // untestable. Pass the instant in. Always.
    private static readonly string[] BannedReads =
    [
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTime.Today",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
    ];

    [Fact]
    public void The_domain_never_reads_the_wall_clock()
    {
        string domainRoot = DomainSourceRoot();

        var offences = new List<string>();

        foreach (string file in Directory.EnumerateFiles(domainRoot, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Comments are where we EXPLAIN this rule, so they cannot be where we break it. The
                // scan reads code, not prose — otherwise the doc comment on the fix would trip the
                // test that motivated the fix, which is the kind of own-goal that gets fitness
                // functions deleted by the next person in a hurry.
                if (IsComment(line))
                {
                    continue;
                }

                foreach (string banned in BannedReads)
                {
                    // Word-boundary matched so `DateTime.UtcNow` does not also flag a hypothetical
                    // `MyDateTime.UtcNowIsh`, and so the ban is about the API rather than the letters.
                    if (Regex.IsMatch(line, $@"(?<![\w.]){Regex.Escape(banned)}\b"))
                    {
                        offences.Add($"{Path.GetRelativePath(domainRoot, file)}:{i + 1}: {banned} — {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            $"""
             Agora.Domain read the wall clock in {offences.Count} place(s).

             {string.Join(Environment.NewLine, offences)}

             The domain takes the instant as a PARAMETER — it never fetches one. The instant is read
             once, at the edge, from the injected TimeProvider (see Application/Pricing/ProductPricing),
             and handed down. A domain function that reads the clock itself cannot be asked about any
             moment except the one you are standing in, which is why it cannot be tested and why it
             disagrees with everything around it.

             See docs/adr/0008-the-clock-is-a-port.md and docs/concepts/the-clock.md.
             """);
    }

    // WHY walk up to find the repo rather than hardcode a relative path: the test binary lives in
    // bin/Debug/net10.0, and how deep that is from the solution root is a build detail that has
    // already changed once in this repo's life. Anchoring on a file that defines the repository (the
    // solution) makes this work from any working directory a runner might pick.
    private static string DomainSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Agora.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string domain = Path.Combine(directory.FullName, "src", "Agora.Domain");

        Assert.True(Directory.Exists(domain), $"Could not find the domain sources at {domain}.");

        return domain;
    }

    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();

        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal);
    }
}
