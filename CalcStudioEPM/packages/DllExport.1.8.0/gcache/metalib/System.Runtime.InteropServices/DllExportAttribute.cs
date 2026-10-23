// Gcache stub: equivalent to what DllExport.bat -action Configure generates.
// Assembly name must be "DllExport"; namespace must match DllExportNamespace.
// The IL weaver (net.r_eg.DllExport.Activator) finds this type by FullName and
// by assembly scope ("DllExport") — both checks must match.
using System;

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class DllExportAttribute : Attribute
    {
        public string ExportName { get; private set; }
        public CallingConvention CallingConvention { get; set; }

        public DllExportAttribute() : this(null) { }

        public DllExportAttribute(string exportName)
            : this(exportName, CallingConvention.Cdecl) { }

        public DllExportAttribute(string exportName, CallingConvention callingConvention)
        {
            ExportName = exportName;
            CallingConvention = callingConvention;
        }
    }
}
