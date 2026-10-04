// Package Search: searches GitHub for VRChat packages and installs the VPM ones. Full docs: CLAUDE.md > Tools > Package Search.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace HaTools
{
    public partial class PackageSearch : EditorWindow
    {
        const string Title = "Package Search";

        [MenuItem(HaToolsMenu.Root + Title)]
        static void Open() => GetWindow<PackageSearch>(Title);

        // ---------------------------------------------------------------- Window

        static readonly string[] Tabs = { "Search", "Installed" };
        const string QueryControl = "HaTools.PackageSearch.Query";

        [SerializeField] int tab;
        [SerializeField] string query = "";
        [SerializeField] List<Result> results = new List<Result>();
        [SerializeField] string status; // summary or error of the last search
        [SerializeField] bool statusIsError;
        [NonSerialized] UnityWebRequest request; // the search in progress, if any
        Vector2 scroll;

        void OnEnable()
        {
            // Checks don't survive a script reload: start the unfinished ones again
            if (results.Any(r => r.Vpm == VpmState.Unchecked || r.Vpm == VpmState.Checking)) StartChecks();
        }

        void OnDisable()
        {
            CancelSearch();
            CancelChecks();
        }

        void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, Tabs);
            EditorGUILayout.Space();
            if (tab == 0) SearchTab();
            else InstalledTab();
        }

        void SearchTab()
        {
            // Search on Enter or the button only, never per keystroke: one API call per search
            var e = Event.current;
            bool search = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == QueryControl;
            if (search) e.Use();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.SetNextControlName(QueryControl);
                query = EditorGUILayout.TextField(query);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(query)))
                    search |= GUILayout.Button("Search", GUILayout.Width(70));
            }
            if (search) StartSearch();

            if (request != null) EditorGUILayout.HelpBox("Searching...", MessageType.None);
            else if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusIsError ? MessageType.Warning : MessageType.None);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in results) DrawResult(r);
            EditorGUILayout.EndScrollView();
        }

        static void DrawResult(Result r)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(r.FullName, EditorStyles.boldLabel);
                    GUILayout.Label($"{r.Stars} stars", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                }
                if (!string.IsNullOrEmpty(r.Description)) EditorGUILayout.LabelField(r.Description, EditorStyles.wordWrappedMiniLabel);
                DrawVpm(r);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (r.Vpm == VpmState.Found)
                    {
                        var back = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.45f, 0.9f, 0.45f);
                        using (new EditorGUI.DisabledScope(true)) // Install isn't built yet
                            GUILayout.Button(new GUIContent("Install", "Not built yet"), GUILayout.Width(80));
                        GUI.backgroundColor = back;
                    }
                    if (GUILayout.Button("Open in Browser", GUILayout.Width(120))) Application.OpenURL(r.Url);
                }
            }
        }

        static void DrawVpm(Result r)
        {
            var p = r.Package;
            switch (r.Vpm)
            {
                case VpmState.Checking:
                    EditorGUILayout.LabelField("Looking for a VPM package...", EditorStyles.miniLabel);
                    break;
                case VpmState.NotFound:
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(r.VpmNote) ? "No VPM package found." : r.VpmNote, EditorStyles.wordWrappedMiniLabel);
                    break;
                case VpmState.Found:
                    string from = p.Source == VpmSource.Listing ? $"from listing {p.ListingUrl}" : "from the latest release (no listing found)";
                    EditorGUILayout.LabelField($"VPM: {p.Name} {p.Version}, {from}", EditorStyles.wordWrappedMiniLabel);
                    if (p.Dependencies.Count > 0)
                        EditorGUILayout.LabelField("Needs: " + string.Join(", ", p.Dependencies.Select(d => $"{d.Name} {d.Range}")), EditorStyles.wordWrappedMiniLabel);
                    break;
            }
        }

        // Packages installed through VPM, read from Packages/vpm-manifest.json (not built yet)
        void InstalledTab()
        {
            EditorGUILayout.HelpBox("Not built yet: this tab will list the packages in Packages/vpm-manifest.json.", MessageType.Info);
        }

        // ---------------------------------------------------------------- Results

        [Serializable]
        internal class Result
        {
            public string FullName;      // owner/repo
            public string Description;   // empty when the repo has none
            public int Stars;
            public string Url;           // the repo's GitHub page
            public string DefaultBranch; // where to look for package.json
            public VpmState Vpm;         // set by the VPM check (PackageSearch.Vpm.cs)
            public VpmPackage Package;   // only meaningful when Vpm is Found (Unity never serializes it as null)
            public string VpmNote;       // why a NotFound repo can't be installed, when there is more to say
        }

        // ---------------------------------------------------------------- GitHub search

        const string SearchApi = "https://api.github.com/search/repositories";
        // Qualifiers are ANDed, so only one: topic:vrchat topic:vpm would need repos tagged with both
        internal const string Qualifier = "topic:vrchat";
        internal const int MaxResults = 30;
        const string UserAgent = "HaTools"; // GitHub rejects requests without one

        internal static string SearchUrl(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            return $"{SearchApi}?q={Uri.EscapeDataString(query.Trim() + " " + Qualifier)}&per_page={MaxResults}";
        }

        void StartSearch()
        {
            string url = SearchUrl(query);
            if (url == null) return;
            CancelSearch();
            results.Clear();
            scroll = Vector2.zero;
            var r = UnityWebRequest.Get(url);
            r.SetRequestHeader("Accept", "application/vnd.github+json");
            r.SetRequestHeader("User-Agent", UserAgent);
            request = r;
            r.SendWebRequest().completed += _ => SearchDone(r);
        }

        void CancelSearch()
        {
            if (request == null) return;
            request.Abort();
            request.Dispose();
            request = null;
        }

        void SearchDone(UnityWebRequest r)
        {
            if (r != request) return; // cancelled (and disposed) or replaced by a newer search
            request = null;
            using (r)
            {
                if (r.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        results = ParseResults(r.downloadHandler.text, out int total);
                        status = results.Count == 0 ? "Nothing found."
                            : total > results.Count ? $"Showing the first {results.Count} of {total}." : $"Found {results.Count}.";
                        statusIsError = false;
                        StartChecks();
                    }
                    catch (ArgumentException)
                    {
                        status = "GitHub's reply couldn't be read.";
                        statusIsError = true;
                    }
                }
                else
                {
                    status = ErrorMessage(r.responseCode, r.error, r.GetResponseHeader("X-RateLimit-Remaining"),
                        r.GetResponseHeader("X-RateLimit-Reset"), DateTimeOffset.UtcNow);
                    statusIsError = true;
                }
            }
            Repaint();
        }

        // Only the fields we use; JsonUtility skips the rest. Filled by JsonUtility, hence no CS0649 "never assigned".
#pragma warning disable 0649
        [Serializable] class SearchJson { public int total_count; public RepoJson[] items; }
        [Serializable] class RepoJson { public string full_name, description, html_url, default_branch; public int stargazers_count; }
#pragma warning restore 0649

        internal static List<Result> ParseResults(string json, out int total)
        {
            var parsed = JsonUtility.FromJson<SearchJson>(json);
            total = parsed?.total_count ?? 0;
            if (parsed?.items == null) return new List<Result>();
            return parsed.items.Take(MaxResults).Select(i => new Result
            {
                FullName = i.full_name,
                Description = i.description ?? "",
                Stars = i.stargazers_count,
                Url = i.html_url,
                DefaultBranch = i.default_branch,
            }).ToList();
        }

        // Unsigned-in search allows 10 requests a minute; X-RateLimit-Reset is when it refills (Unix seconds)
        internal static string ErrorMessage(long code, string error, string remaining, string reset, DateTimeOffset now)
        {
            if (code == 0) return $"Couldn't reach GitHub: {error}";
            if ((code == 403 || code == 429) && remaining == "0")
            {
                string wait = long.TryParse(reset, out long at) ? $"in {Math.Max(1, at - now.ToUnixTimeSeconds())} seconds" : "in a minute";
                return $"GitHub's search limit (10 searches a minute) is used up. Try again {wait}.";
            }
            if (code == 422) return "GitHub couldn't run this search. Check the query for unbalanced quotes or a wrong qualifier.";
            return $"GitHub search failed ({code}): {error}";
        }
    }
}
