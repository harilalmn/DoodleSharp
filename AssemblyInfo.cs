using System.Runtime.CompilerServices;
using System.Windows;

// Lets the test project reach internal test hooks such as Journal.ResetDirectoryCache().
[assembly: InternalsVisibleTo("DoodleSharp.Tests")]
// The benchmark harness drives the viewport and reads frame metrics directly. It is a
// development tool, built by CI but never shipped, so this stays off the public API.
[assembly: InternalsVisibleTo("DoodleSharp.Bench")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
