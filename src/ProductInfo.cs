using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("Grasshopper Thumbnails & Preview")]
[assembly: AssemblyDescription("Embedded bitmap thumbnails and previews for .gh and .ghx files in Windows Explorer.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion(GrasshopperFilePreview.ProductInfo.Version)]
[assembly: AssemblyProduct("Grasshopper Thumbnails & Preview")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Grasshopper Thumbnails & Preview contributors")]
[assembly: ComVisible(false)]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace GrasshopperFilePreview
{
    internal static class ProductInfo
    {
        internal const string Version = "1.0";
        internal const int ReadTimeoutMilliseconds = 15000;
    }
}
