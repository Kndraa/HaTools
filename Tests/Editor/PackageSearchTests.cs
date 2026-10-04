// Package Search tests. Full docs: CLAUDE.md > Tools > Package Search.
using System;
using System.Collections.Generic;
using System.IO;
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

        // ---------------------------------------------------------------- JSON

        [Test]
        public void JsonReadsEveryKindOfValue()
        {
            var o = (Dictionary<string, object>)PackageSearch.Json.Parse(
                "﻿ { \"s\": \"a\\\"b\\\\c\\/d\\n\\u00e9\", \"n\": -1.5e2, \"t\": true, \"f\": false, \"z\": null, \"a\": [1, {}, []], \"o\": {\"k\": \"v\"} } ");
            Assert.AreEqual("a\"b\\c/d\né", o["s"]);
            Assert.AreEqual(-150.0, o["n"]);
            Assert.AreEqual(true, o["t"]);
            Assert.AreEqual(false, o["f"]);
            Assert.IsNull(o["z"]);
            Assert.AreEqual(3, ((List<object>)o["a"]).Count);
            Assert.AreEqual("v", PackageSearch.Json.Str(PackageSearch.Json.Obj(o, "o"), "k"));
            Assert.IsNull(PackageSearch.Json.Obj(o, "s"), "Obj only returns objects");
            Assert.IsNull(PackageSearch.Json.Str(o, "missing"));
        }

        [Test]
        public void JsonRejectsInvalidText()
        {
            foreach (string bad in new[] { null, "", "{", "{\"a\" 1}", "[1,]", "{\"a\":1} x", "\"open", "nul", "<html>" })
                Assert.Throws<FormatException>(() => PackageSearch.Json.Parse(bad), bad ?? "null");
        }

        // ---------------------------------------------------------------- Versions

        [TestCase("1.10.0", "1.9.0", 1)]
        [TestCase("1.0.0", "1.0.0-beta", 1)]
        [TestCase("1.0.0-beta.2", "1.0.0-beta.11", -1)]
        [TestCase("1.0.0-rc.1", "1.0.0-beta.9", 1)]
        [TestCase("1.0.0-alpha", "1.0.0-alpha.1", -1)]
        [TestCase("1.0.0-1", "1.0.0-alpha", -1)]
        [TestCase("1.2", "1.2.0", 0)]
        [TestCase("1.0.0+build.5", "1.0.0", 0)]
        public void VersionsCompareBySemver(string a, string b, int expected)
        {
            Assert.AreEqual(expected, Math.Sign(PackageSearch.CompareVersions(a, b)));
            Assert.AreEqual(-expected, Math.Sign(PackageSearch.CompareVersions(b, a)));
        }

        [Test]
        public void NewestVersionSkipsPreReleasesWhenThereIsARelease()
        {
            Assert.AreEqual("1.1.3", PackageSearch.NewestVersion(new[] { "1.0.0", "1.2.0-beta.1", "1.1.3", "0.9.0" }));
            Assert.AreEqual("2.0.0-beta.10", PackageSearch.NewestVersion(new[] { "2.0.0-beta.1", "2.0.0-beta.10" }));
            Assert.IsNull(PackageSearch.NewestVersion(new string[0]));
        }

        // ---------------------------------------------------------------- Latest release

        [Test]
        public void ReleasePackageJsonGivesPackageAndZip()
        {
            var p = PackageSearch.ParseReleasePackage(
                "{\"name\":\"com.example.tool\",\"displayName\":\"Tool\",\"version\":\"1.2.3\",\"url\":\"https://example.com\"," +
                "\"vpmDependencies\":{\"com.vrchat.avatars\":\">=3.7.0\",\"nadena.dev.ndmf\":\"^1.0.0\"}}", "owner/tool");
            Assert.AreEqual("com.example.tool", p.Name);
            Assert.AreEqual("Tool", p.DisplayName);
            Assert.AreEqual("1.2.3", p.Version);
            Assert.AreEqual("https://github.com/owner/tool/releases/latest/download/com.example.tool-1.2.3.zip", p.ZipUrl, "A url that isn't a zip is ignored");
            Assert.AreEqual(PackageSearch.VpmSource.Release, p.Source);
            Assert.AreEqual("", p.ListingUrl);
            CollectionAssert.AreEqual(new[] { "com.vrchat.avatars >=3.7.0", "nadena.dev.ndmf ^1.0.0" }, p.Dependencies.Select(d => d.Name + " " + d.Range));
        }

        [Test]
        public void ReleasePackageJsonUsesItsZipUrl()
        {
            var p = PackageSearch.ParseReleasePackage("{\"name\":\"a\",\"version\":\"1.0.0\",\"url\":\"https://cdn.example.com/a-1.0.0.zip\"}", "o/r");
            Assert.AreEqual("https://cdn.example.com/a-1.0.0.zip", p.ZipUrl);
            Assert.AreEqual("a", p.DisplayName, "Without a display name the package name is shown");
        }

        [Test]
        public void NoReleasePackageWithoutNameAndVersion()
        {
            Assert.IsNull(PackageSearch.ParseReleasePackage(null, "o/r"), "No release or no package.json");
            Assert.IsNull(PackageSearch.ParseReleasePackage("Not Found", "o/r"));
            Assert.IsNull(PackageSearch.ParseReleasePackage("{\"name\":\"a\"}", "o/r"));
            Assert.IsNull(PackageSearch.ParseReleasePackage("[]", "o/r"));
        }

        // ---------------------------------------------------------------- Where to look for a listing

        [Test]
        public void ListingCandidatesComeFromReadmeThenOwnPages()
        {
            string readme = string.Join("\n",
                "[Add to VCC](vcc://vpm/addRepo?url=https%3A%2F%2Fvpm.example.com%2Fvcc)",
                "![badge](https://img.shields.io/vpm/v/com.x?repository_url=https%3A%2F%2Fvpm.other.com%2Fvpm.json)",
                "Listing: `https://example.github.io/listing/index.json`.",
                "Again https://vpm.other.com/vpm.json",
                "Not these: https://github.com/Owner/Repo/blob/main/listing.json https://example.com/package.json https://example.com/page");
            CollectionAssert.AreEqual(new[]
            {
                "https://vpm.example.com/vcc",
                "https://vpm.other.com/vpm.json",
                "https://example.github.io/listing/index.json",
                "https://owner.github.io/Repo/index.json",
            }, PackageSearch.ListingCandidates("Owner/Repo", readme));
        }

        [Test]
        public void ListingCandidatesAreCapped()
        {
            string readme = string.Join(" ", Enumerable.Range(0, 10).Select(i => $"https://l{i}.example.com/index.json"));
            var list = PackageSearch.ListingCandidates("o/r", readme);
            Assert.AreEqual(4, list.Count, "Three from the README plus the repo's own pages");
            CollectionAssert.AreEqual(new[] { "https://o.github.io/r/index.json" }, PackageSearch.ListingCandidates("o/r", null));
        }

        // ---------------------------------------------------------------- Finding the package in a listing

        static string Version(string name, string version, string url) =>
            $"\"{version}\":{{\"name\":\"{name}\",\"version\":\"{version}\",\"displayName\":\"{name} display\",\"url\":\"{url}\",\"vpmDependencies\":{{\"dep\":\">=1.0.0\"}}}}";

        static object ListingJson(params string[] packages) =>
            PackageSearch.Json.Parse("{\"name\":\"L\",\"url\":\"https://l.example.com/index.json\",\"packages\":{" + string.Join(",", packages) + "}}");

        static string Package(string name, params string[] versions) => $"\"{name}\":{{\"versions\":{{{string.Join(",", versions)}}}}}";

        static readonly object TwoPackages = ListingJson(
            Package("com.a", Version("com.a", "1.0.0", "https://github.com/owner/a/releases/download/1.0.0/a.zip"),
                Version("com.a", "1.1.0", "https://github.com/owner/a/releases/download/1.1.0/a.zip"),
                Version("com.a", "2.0.0-beta.1", "https://github.com/owner/a/releases/download/2.0.0-beta.1/a.zip")),
            Package("com.b", Version("com.b", "3.0.0", "https://cdn.example.com/b-3.0.0.zip")));

        [Test]
        public void PackageFoundByDownloadAddress()
        {
            var p = PackageSearch.FindInListing(TwoPackages, "https://l.example.com/index.json", "Owner/A", null, false);
            Assert.AreEqual("com.a", p.Name);
            Assert.AreEqual("1.1.0", p.Version, "Newest release, not the pre-release");
            Assert.AreEqual("https://github.com/owner/a/releases/download/1.1.0/a.zip", p.ZipUrl);
            Assert.AreEqual("com.a display", p.DisplayName);
            Assert.AreEqual(PackageSearch.VpmSource.Listing, p.Source);
            Assert.AreEqual("https://l.example.com/index.json", p.ListingUrl);
            Assert.AreEqual("dep", p.Dependencies.Single().Name);
        }

        [Test]
        public void PackageFoundByName()
        {
            Assert.AreEqual("com.b", PackageSearch.FindInListing(TwoPackages, "u", "owner/b", "com.b", false).Name, "Downloads hosted elsewhere");
        }

        [Test]
        public void OwnListingWithOnePackageMatches()
        {
            var one = ListingJson(Package("com.b", Version("com.b", "3.0.0", "https://cdn.example.com/b-3.0.0.zip")));
            Assert.IsNull(PackageSearch.FindInListing(one, "u", "owner/b", null, false));
            Assert.AreEqual("com.b", PackageSearch.FindInListing(one, "u", "owner/b", null, true).Name);
            Assert.IsNull(PackageSearch.FindInListing(TwoPackages, "u", "owner/c", null, true), "Two packages: can't tell which");
        }

        [Test]
        public void UnrelatedOrBrokenListingsMatchNothing()
        {
            Assert.IsNull(PackageSearch.FindInListing(TwoPackages, "u", "someone/else", "com.else", false));
            Assert.IsNull(PackageSearch.FindInListing(null, "u", "owner/a", "com.a", false));
            Assert.IsNull(PackageSearch.FindInListing(PackageSearch.Json.Parse("{\"packages\":{\"com.a\":{\"versions\":{}}}}"), "u", "owner/a", "com.a", false));
            Assert.IsNull(PackageSearch.FindInListing(PackageSearch.Json.Parse("{\"packages\":{\"com.a\":{\"versions\":{\"1.0.0\":{\"name\":\"com.a\",\"version\":\"1.0.0\"}}}}}"),
                "u", "owner/a", "com.a", false), "A version without a download");
        }

        // ---------------------------------------------------------------- Listings known to the VCC

        string folder;

        [TearDown]
        public void DeleteFolder()
        {
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true);
            folder = null;
        }

        [Test]
        public void VccListingsAreReadFromItsCache()
        {
            folder = Path.Combine(Path.GetTempPath(), "HaTools.PackageSearchTests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string listing = "{\"url\":\"https://l.example.com/index.json\",\"packages\":{}}";
            File.WriteAllText(Path.Combine(folder, "cached.json"), "{\"repo\":" + listing + ",\"headers\":{}}");
            File.WriteAllText(Path.Combine(folder, "bare.json"), listing.Replace("l.example", "bare.example"));
            File.WriteAllText(Path.Combine(folder, "no-url.json"), "{\"repo\":{\"packages\":{}}}");
            File.WriteAllText(Path.Combine(folder, "broken.json"), "{");
            File.WriteAllText(Path.Combine(folder, "settings.txt"), listing);

            var urls = PackageSearch.LoadVccListings(folder).Select(l => l.Url).OrderBy(u => u);
            CollectionAssert.AreEqual(new[] { "https://bare.example.com/index.json", "https://l.example.com/index.json" }, urls);
            Assert.IsEmpty(PackageSearch.LoadVccListings(Path.Combine(folder, "missing")));
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
