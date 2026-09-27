// Core tests: checks the conventions every tool shares. Full docs: CLAUDE.md > Testing.
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;

namespace HaTools.Tests
{
    public class CoreTests
    {
        static readonly Assembly ToolsAssembly = typeof(HaToolsMenu).Assembly;

        [Test]
        public void MenuRootIsUnderTools()
        {
            StringAssert.StartsWith("Tools/", HaToolsMenu.Root);
            StringAssert.EndsWith("/", HaToolsMenu.Root);
        }

        [Test]
        public void ToolsMenuItemsUseHaToolsMenuRoot()
        {
            // Context menus (e.g. "Assets/...") are allowed; anything under Tools/ must sit under our root
            var wrong = ToolsAssembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .SelectMany(m => m.GetCustomAttributes<MenuItem>())
                .Select(a => a.menuItem)
                .Where(p => p.StartsWith("Tools/") && !p.StartsWith(HaToolsMenu.Root))
                .ToList();
            Assert.IsEmpty(wrong, "Build menu paths from HaToolsMenu.Root: " + string.Join(", ", wrong));
        }

        [Test]
        public void AllTypesUseHaToolsNamespace()
        {
            // Top-level types only (nested ones share their parent's namespace); skip compiler/Unity-generated ones
            var wrong = ToolsAssembly.GetTypes()
                .Where(t => !t.IsNested && !t.IsDefined(typeof(CompilerGeneratedAttribute), false) && !t.Name.StartsWith("<"))
                .Where(t => t.Namespace == null || !(t.Namespace == "HaTools" || t.Namespace.StartsWith("HaTools.")))
                .Select(t => t.FullName)
                .ToList();
            Assert.IsEmpty(wrong, "Put these types in the HaTools namespace: " + string.Join(", ", wrong));
        }
    }
}
