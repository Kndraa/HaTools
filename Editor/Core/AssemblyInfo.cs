// Lets the tests call tools' internal methods. Full notes: CLAUDE.md > Testing.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("HaTools.Editor.Tests")]
