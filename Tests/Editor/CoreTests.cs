// Core tests: checks the conventions every tool shares. Full docs: CLAUDE.md > Testing.
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;

namespace Kndra.Tools.Tests
{
    public class CoreTests
    {
        static readonly Assembly ToolsAssembly = typeof(KndraMenu).Assembly;

        [Test]
        public void MenuRootIsUnderTools()
        {
            StringAssert.StartsWith("Tools/", KndraMenu.Root);
            StringAssert.EndsWith("/", KndraMenu.Root);
        }

        [Test]
        public void ToolsMenuItemsUseKndraMenuRoot()
        {
            // Context menus (e.g. "Assets/...") are allowed; anything under Tools/ must sit under our root
            var wrong = ToolsAssembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .SelectMany(m => m.GetCustomAttributes<MenuItem>())
                .Select(a => a.menuItem)
                .Where(p => p.StartsWith("Tools/") && !p.StartsWith(KndraMenu.Root))
                .ToList();
            Assert.IsEmpty(wrong, "Build menu paths from KndraMenu.Root: " + string.Join(", ", wrong));
        }

        [Test]
        public void AllTypesUseKndraToolsNamespace()
        {
            var wrong = ToolsAssembly.GetTypes()
                .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false) && !t.Name.StartsWith("<"))
                .Where(t => t.Namespace == null || !(t.Namespace == "Kndra.Tools" || t.Namespace.StartsWith("Kndra.Tools.")))
                .Select(t => t.FullName)
                .ToList();
            Assert.IsEmpty(wrong, "Put these types in the Kndra.Tools namespace: " + string.Join(", ", wrong));
        }
    }
}
