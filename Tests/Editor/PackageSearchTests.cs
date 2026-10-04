// Package Search tests. Full docs: CLAUDE.md > Tools > Package Search.
using System;
using System.Linq;
using NUnit.Framework;

namespace HaTools.Tests
{
    public class PackageSearchTests
    {
        // ---------------------------------------------------------------- Search URL

        [Test]
        public void SearchUrlAddsQualifierAndLimit()
        {
            string url = PackageSearch.SearchUrl("  avatar tools ");
            StringAssert.StartsWith("https://api.github.com/search/repositories?q=", url);
            StringAssert.Contains("q=" + Uri.EscapeDataString("avatar tools " + PackageSearch.Qualifier) + "&", url);
            StringAssert.EndsWith("&per_page=" + PackageSearch.MaxResults, url);
        }

        [Test]
        public void SearchUrlEscapesSpecialCharacters()
        {
            string url = PackageSearch.SearchUrl("a&b=c #d");
            StringAssert.DoesNotContain("&b=", url);
            StringAssert.DoesNotContain("#", url);
        }

        [Test]
        public void BlankQueryHasNoUrl()
        {
            Assert.IsNull(PackageSearch.SearchUrl(""));
            Assert.IsNull(PackageSearch.SearchUrl("   "));
            Assert.IsNull(PackageSearch.SearchUrl(null));
        }

        // ---------------------------------------------------------------- Parsing

        static string Repo(string name, string description, int stars) =>
            "{\"name\":\"" + name + "\",\"full_name\":\"owner/" + name + "\",\"html_url\":\"https://github.com/owner/" + name + "\"," +
            "\"description\":" + (description == null ? "null" : "\"" + description + "\"") + "," +
            "\"stargazers_count\":" + stars + ",\"default_branch\":\"main\",\"owner\":{\"login\":\"owner\"},\"topics\":[\"vrchat\"],\"license\":null}";

        static string Reply(int total, params string[] repos) =>
            "{\"total_count\":" + total + ",\"incomplete_results\":false,\"items\":[" + string.Join(",", repos) + "]}";

        [Test]
        public void ParseReadsRepoFields()
        {
            var results = PackageSearch.ParseResults(Reply(2, Repo("tool", "A tool", 42), Repo("other", null, 0)), out int total);
            Assert.AreEqual(2, total);
            Assert.AreEqual(2, results.Count);
            var r = results[0];
            Assert.AreEqual("owner/tool", r.FullName);
            Assert.AreEqual("A tool", r.Description);
            Assert.AreEqual(42, r.Stars);
            Assert.AreEqual("https://github.com/owner/tool", r.Url);
            Assert.AreEqual("main", r.DefaultBranch);
            Assert.AreEqual(PackageSearch.VpmState.Unchecked, r.Vpm);
            Assert.AreEqual("", results[1].Description, "A null description becomes empty");
        }

        [Test]
        public void ParseKeepsAtMostMaxResults()
        {
            var repos = Enumerable.Range(0, PackageSearch.MaxResults + 5).Select(i => Repo("r" + i, "", i)).ToArray();
            var results = PackageSearch.ParseResults(Reply(500, repos), out int total);
            Assert.AreEqual(PackageSearch.MaxResults, results.Count);
            Assert.AreEqual(500, total);
            Assert.AreEqual("owner/r0", results[0].FullName, "GitHub's order is kept");
        }

        [Test]
        public void ParseWithoutItemsIsEmpty()
        {
            Assert.IsEmpty(PackageSearch.ParseResults(Reply(0), out int total));
            Assert.AreEqual(0, total);
            Assert.IsEmpty(PackageSearch.ParseResults("{}", out _));
        }

        // ---------------------------------------------------------------- Errors

        static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        [Test]
        public void RateLimitSaysWhenToRetry()
        {
            string msg = PackageSearch.ErrorMessage(403, "HTTP/1.1 403 Forbidden", "0", "1800000042", Now);
            StringAssert.Contains("limit", msg);
            StringAssert.Contains("42 seconds", msg);
            StringAssert.Contains("in a minute", PackageSearch.ErrorMessage(429, "", "0", null, Now));
        }

        [Test]
        public void OtherErrorsAreDescribed()
        {
            StringAssert.StartsWith("Couldn't reach GitHub", PackageSearch.ErrorMessage(0, "Cannot resolve destination host", null, null, Now));
            StringAssert.Contains("query", PackageSearch.ErrorMessage(422, "", null, null, Now));
            StringAssert.Contains("403", PackageSearch.ErrorMessage(403, "Forbidden", "5", null, Now), "A 403 with requests left isn't the rate limit");
            StringAssert.Contains("500", PackageSearch.ErrorMessage(500, "Internal Server Error", null, null, Now));
        }
    }
}
