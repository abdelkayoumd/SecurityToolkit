using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("SecurityToolkit.Tests")]

[assembly: AssemblyTitle("Security Toolkit")]
[assembly: AssemblyDescription("One place for Dataverse security administration: users, teams, business units and security roles.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("abdelkayoumd")]
[assembly: AssemblyProduct("SecurityToolkit")]
[assembly: AssemblyCopyright("Copyright © 2026 abdelkayoumd")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]
[assembly: Guid("5b0f3c1e-7d64-4a52-9c8e-31f0a6d2b7e4")]

#if !DEBUG
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]
#else
// Local builds get a new revision every time: XrmToolBox caches its plugin scan per assembly version
// (Plugins\manifest.json), so a build with an unchanged version would never be looked at again.
// The release version above stays first in this file: the release workflow reads the first match.
[assembly: AssemblyVersion("1.5.0.*")]
[assembly: AssemblyFileVersion("1.5.0.0")]
#endif
