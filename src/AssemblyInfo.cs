using System.Reflection;
using System.Runtime.CompilerServices;
#if NET
using System.Runtime.Versioning;
#endif

// File properties shown by Windows
[assembly: AssemblyTitle("Deskmate")]
[assembly: AssemblyDescription("Dynamic island untuk Windows")]
[assembly: AssemblyProduct("Deskmate")]
[assembly: AssemblyCompany("R. Revaldo Vabiansyah")]
[assembly: AssemblyCopyright("© 2026 R. Revaldo Vabiansyah")]
[assembly: AssemblyVersion(Deskmate.Island.AppVersion)]
[assembly: AssemblyFileVersion(Deskmate.Island.AppVersion)]
[assembly: AssemblyInformationalVersion(Deskmate.Island.AppVersion)]
[assembly: InternalsVisibleTo("Deskmate.Tests")]
#if NET
[assembly: TargetPlatform("Windows10.0.19041.0")]
[assembly: SupportedOSPlatform("Windows10.0.19041.0")]
#endif
