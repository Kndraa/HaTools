// Package Search: checks each search result for a VPM package (its latest release and its listing). Full docs: CLAUDE.md > Tools > Package Search.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.Networking;

namespace HaTools
{
    public partial class PackageSearch
    {
        // ---------------------------------------------------------------- Model

        internal enum VpmState { Unchecked, Checking, Found, NotFound }

        // Listing: found in a VPM listing, which the VCC can also update it from. Release: only the latest GitHub release has it.
        internal enum VpmSource { Release, Listing }

        [Serializable]
        internal class Dependency
        {
            public string Name;
            public string Range; // e.g. ">=1.2.0 <2.0.0", as written in vpmDependencies
        }

        [Serializable]
        internal class VpmPackage
        {
            public string Name;        // e.g. com.example.tool
            public string DisplayName;
            public string Version;     // newest release version
            public string ZipUrl;      // that version's download
            public VpmSource Source;
            public string ListingUrl;  // empty when Source is Release
            public List<Dependency> Dependencies = new List<Dependency>();
        }

        // A listing read from disk or the web, with the address the VCC knows it by
        internal class Listing
        {
            public string Url;
            public object Root;
        }

        // ---------------------------------------------------------------- Check

        const int MaxRequests = 6; // at once, across every result
        const int MaxReadmeListings = 3;

        [NonSerialized] int generation; // bumped by every search, so checks from an older one stop
        [NonSerialized] List<Listing> vccListings = new List<Listing>();

        void StartChecks()
        {
            generation++;
            ClearQueue();
            vccListings = LoadVccListings(VccReposFolder);
            foreach (var r in results)
                if (r.Vpm == VpmState.Unchecked || r.Vpm == VpmState.Checking) Check(r, generation);
        }

        // Fetches the release's package.json and the README together, then looks for a listing that has the package
        void Check(Result r, int gen)
        {
            r.Vpm = VpmState.Checking;
            string release = null, readme = null;
            int left = 2;
            Action next = () =>
            {
                if (--left > 0 || gen != generation) return;
                var fromRelease = ParseReleasePackage(release, r.FullName);
                string name = fromRelease?.Name;
                // Listings already added to the VCC need no download
                var found = vccListings.Select(l => FindInListing(l.Root, l.Url, r.FullName, name, false)).FirstOrDefault(p => p != null);
                if (found != null) Finish(r, found, null);
                else TryListings(r, gen, ListingCandidates(r.FullName, readme), 0, fromRelease);
            };
            Get(ReleaseFileUrl(r.FullName, "package.json"), false, t => { release = t; next(); });
            Get(ReadmeUrl(r), false, t => { readme = t; next(); });
        }

        void TryListings(Result r, int gen, List<string> candidates, int index, VpmPackage fromRelease)
        {
            if (index < candidates.Count)
            {
                string url = candidates[index];
                Get(url, false, text =>
                {
                    if (gen != generation) return;
                    object root = null;
                    try { root = Json.Parse(text); } catch (FormatException) { }
                    var found = FindInListing(root, url, r.FullName, fromRelease?.Name, url == OwnPagesListing(r.FullName));
                    if (found != null) Finish(r, found, null);
                    else TryListings(r, gen, candidates, index + 1, fromRelease);
                });
                return;
            }
            if (fromRelease == null) { Finish(r, null, null); return; }
            // Only offer the release if its zip is really there
            Get(fromRelease.ZipUrl, true, text =>
            {
                if (gen != generation) return;
                if (text != null) Finish(r, fromRelease, null);
                else Finish(r, null, $"The latest release has a package.json but no {Path.GetFileName(fromRelease.ZipUrl)}.");
            });
        }

        static void Finish(Result r, VpmPackage package, string note)
        {
            r.Vpm = package != null ? VpmState.Found : VpmState.NotFound;
            r.Package = package ?? new VpmPackage();
            r.VpmNote = note ?? "";
        }

        // ---------------------------------------------------------------- Where to look

        internal static string ReleaseFileUrl(string fullName, string file) => $"https://github.com/{fullName}/releases/latest/download/{file}";

        static string ReadmeUrl(Result r) =>
            $"https://raw.githubusercontent.com/{r.FullName}/{(string.IsNullOrEmpty(r.DefaultBranch) ? "HEAD" : r.DefaultBranch)}/README.md";

        // Where VRChat's package template publishes a listing when the repo hosts its own
        internal static string OwnPagesListing(string fullName)
        {
            int slash = fullName.IndexOf('/');
            return $"https://{fullName.Substring(0, slash).ToLowerInvariant()}.github.io/{fullName.Substring(slash + 1)}/index.json";
        }

        // Listing addresses a README names: "Add to VCC" links (vcc://vpm/addRepo?url=...) and .json links, also URL-encoded ones
        // such as shields.io badges; then the repo's own GitHub Pages listing
        static readonly Regex Link = new Regex(@"(vcc://vpm/addRepo\?url=)?(https?://[^\s()""'<>\[\]`]+)", RegexOptions.IgnoreCase);
        static readonly string[] NotListings = { "package.json", "package-lock.json", "manifest.json", "vpm-manifest.json" };

        internal static List<string> ListingCandidates(string fullName, string readme)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(readme))
                foreach (Match m in Link.Matches(Uri.UnescapeDataString(readme)))
                {
                    string url = m.Groups[2].Value.TrimEnd('.', ',', ';', ':');
                    // A badge carries the listing in its query (...?repository_url=https://...): keep the innermost address
                    int inner = Math.Max(url.LastIndexOf("https://", StringComparison.OrdinalIgnoreCase), url.LastIndexOf("http://", StringComparison.OrdinalIgnoreCase));
                    url = url.Substring(inner);
                    bool vccLink = m.Groups[1].Success;
                    if (!vccLink && !url.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (NotListings.Any(n => url.EndsWith("/" + n, StringComparison.OrdinalIgnoreCase))) continue;
                    if (Regex.IsMatch(url, @"^https?://github\.com/[^/]+/[^/]+/blob/", RegexOptions.IgnoreCase)) continue; // a web page
                    if (!list.Contains(url)) list.Add(url);
                    if (list.Count == MaxReadmeListings) break;
                }
            string own = OwnPagesListing(fullName);
            if (!list.Contains(own)) list.Add(own);
            return list;
        }

        // The VCC (and vrc-get / ALCOM) keep each listing they know in Repos/*.json as {"repo": <listing>, ...}
        internal static string VccReposFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRChatCreatorCompanion", "Repos");

        internal static List<Listing> LoadVccListings(string folder)
        {
            var list = new List<Listing>();
            if (!Directory.Exists(folder)) return list;
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    object root = Json.Parse(File.ReadAllText(file));
                    object listing = Json.Obj(root, "repo") ?? root;
                    string url = Json.Str(listing, "url");
                    if (!string.IsNullOrEmpty(url) && Json.Obj(listing, "packages") != null) list.Add(new Listing { Url = url, Root = listing });
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException) { }
            }
            return list;
        }

        // ---------------------------------------------------------------- Reading packages

        // The package.json attached to the latest release (VRChat's package template does this), with its zip beside it
        internal static VpmPackage ParseReleasePackage(string json, string fullName)
        {
            object root;
            try { root = Json.Parse(json); } catch (FormatException) { return null; }
            var p = ReadPackage(root);
            if (p == null) return null;
            string url = Json.Str(root, "url");
            p.ZipUrl = url != null && url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? url : ReleaseFileUrl(fullName, $"{p.Name}-{p.Version}.zip");
            p.Source = VpmSource.Release;
            p.ListingUrl = "";
            return p;
        }

        // The package in a listing that belongs to this repo: the one with the release's package name, else one whose downloads
        // come from this repo, else the only package of the repo's own listing. Its newest version is the one offered.
        internal static VpmPackage FindInListing(object listing, string listingUrl, string fullName, string packageName, bool ownListing)
        {
            var packages = Json.Obj(listing, "packages");
            if (packages == null) return null;
            string repoPath = $"github.com/{fullName}/";
            Dictionary<string, object> match = null;
            if (packageName != null) match = Json.Obj(packages, packageName);
            if (match == null)
                match = packages.Values.OfType<Dictionary<string, object>>().FirstOrDefault(pkg =>
                    Json.Obj(pkg, "versions")?.Values.Any(v => Json.Str(v, "url")?.IndexOf(repoPath, StringComparison.OrdinalIgnoreCase) >= 0) == true);
            if (match == null && ownListing && packages.Count == 1) match = packages.Values.First() as Dictionary<string, object>;

            var versions = Json.Obj(match, "versions");
            string newest = versions == null ? null : NewestVersion(versions.Keys);
            if (newest == null) return null;
            var p = ReadPackage(versions[newest]);
            if (p == null) return null;
            p.ZipUrl = Json.Str(versions[newest], "url") ?? "";
            if (p.ZipUrl == "") return null;
            p.Source = VpmSource.Listing;
            p.ListingUrl = listingUrl;
            return p;
        }

        // Name, version, display name and dependencies of a package.json (also the shape of each version in a listing)
        static VpmPackage ReadPackage(object json)
        {
            string name = Json.Str(json, "name"), version = Json.Str(json, "version");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version)) return null;
            var p = new VpmPackage { Name = name, Version = version, DisplayName = Json.Str(json, "displayName") ?? name };
            var deps = Json.Obj(json, "vpmDependencies");
            if (deps != null)
                foreach (var d in deps) p.Dependencies.Add(new Dependency { Name = d.Key, Range = d.Value as string ?? "" });
            return p;
        }

        // ---------------------------------------------------------------- Versions

        // The highest release, or the highest pre-release when there is no release (the VCC hides pre-releases by default)
        internal static string NewestVersion(IEnumerable<string> versions)
        {
            var all = versions.ToList();
            var releases = all.Where(v => !IsPreRelease(v)).ToList();
            var pool = releases.Count > 0 ? releases : all;
            return pool.Count == 0 ? null : pool.Aggregate((a, b) => CompareVersions(a, b) >= 0 ? a : b);
        }

        static bool IsPreRelease(string v) => StripBuild(v).Contains('-');
        static string StripBuild(string v) => v.Split('+')[0];

        // Semantic versioning order: numbers compared as numbers, a pre-release below its release
        internal static int CompareVersions(string a, string b)
        {
            string[] pa = StripBuild(a).Split(new[] { '-' }, 2), pb = StripBuild(b).Split(new[] { '-' }, 2);
            string[] ca = pa[0].Split('.'), cb = pb[0].Split('.');
            for (int i = 0; i < Math.Max(ca.Length, cb.Length); i++)
            {
                int c = Number(i < ca.Length ? ca[i] : "0").CompareTo(Number(i < cb.Length ? cb[i] : "0"));
                if (c != 0) return c;
            }
            if (pa.Length == 1 || pb.Length == 1) return pb.Length.CompareTo(pa.Length); // the one without a pre-release is higher
            string[] xa = pa[1].Split('.'), xb = pb[1].Split('.');
            for (int i = 0; i < Math.Min(xa.Length, xb.Length); i++)
            {
                bool na = long.TryParse(xa[i], out long a1), nb = long.TryParse(xb[i], out long b1);
                int c = na && nb ? a1.CompareTo(b1) : na ? -1 : nb ? 1 : string.CompareOrdinal(xa[i], xb[i]);
                if (c != 0) return Math.Sign(c);
            }
            return xa.Length.CompareTo(xb.Length);
        }

        static long Number(string s) => long.TryParse(s, out long n) ? n : -1;

        // ---------------------------------------------------------------- Requests

        // Each address is fetched once per window (failures too, except being offline), at most MaxRequests at a time.
        // probe: only checks that the file exists (asks for its first byte). The text is null when the request failed.
        [NonSerialized] readonly Dictionary<string, string> fetched = new Dictionary<string, string>();
        [NonSerialized] readonly Dictionary<string, List<Action<string>>> waiting = new Dictionary<string, List<Action<string>>>();
        [NonSerialized] readonly Queue<(string key, string url, bool probe)> queue = new Queue<(string, string, bool)>();
        [NonSerialized] readonly List<UnityWebRequest> running = new List<UnityWebRequest>();

        void Get(string url, bool probe, Action<string> done)
        {
            string key = (probe ? "probe " : "") + url;
            if (fetched.TryGetValue(key, out string text)) { done(text); return; }
            if (waiting.TryGetValue(key, out var callbacks)) { callbacks.Add(done); return; }
            waiting[key] = new List<Action<string>> { done };
            queue.Enqueue((key, url, probe));
            Pump();
        }

        void Pump()
        {
            while (running.Count < MaxRequests && queue.Count > 0)
            {
                var (key, url, probe) = queue.Dequeue();
                var r = UnityWebRequest.Get(url);
                r.SetRequestHeader("User-Agent", UserAgent);
                if (probe) r.SetRequestHeader("Range", "bytes=0-0");
                running.Add(r);
                r.SendWebRequest().completed += _ => Fetched(r, key);
            }
        }

        void Fetched(UnityWebRequest r, string key)
        {
            if (!running.Remove(r)) return; // aborted
            string text = r.result == UnityWebRequest.Result.Success ? r.downloadHandler.text : null;
            if (r.result != UnityWebRequest.Result.ConnectionError) fetched[key] = text;
            r.Dispose();
            if (waiting.TryGetValue(key, out var callbacks))
            {
                waiting.Remove(key);
                foreach (var done in callbacks) done(text);
            }
            Pump();
            Repaint();
        }

        // Drops requests not started yet; the running ones finish and are kept for later
        void ClearQueue()
        {
            foreach (var q in queue) waiting.Remove(q.key);
            queue.Clear();
        }

        void CancelChecks()
        {
            generation++;
            ClearQueue();
            foreach (var r in running) { r.Abort(); r.Dispose(); }
            running.Clear();
            waiting.Clear();
        }
    }
}
