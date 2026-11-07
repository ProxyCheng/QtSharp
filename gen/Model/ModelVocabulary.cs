using System;

namespace QtSharp.Generator
{
    /// <summary>
    /// The model's vocabulary: the flag words a declaration carries and the shape of a module
    /// descriptor. The extractor writes strings; the passes turn them into these bits once.
    /// </summary>
    public static class ApiModel
    {
        public const string CoreNamespace = "QtSharp.Api";

        [Flags]
        public enum ClassFlags
        {
            None = 0,
            Abstract = 1,
            Subclassable = 2,
            SubclassCtor = 4,
            Owned = 8,
            NonCopyable = 16,
            ToVariant = 32,
        }

        [Flags]
        public enum MethodFlags
        {
            None = 0,
            Static = 1,
            Protected = 2,
            Iterator = 4,
            Const = 8,
            OwnedReturn = 16,
            Slot = 32,
            Invokable = 64,
        }

        [Flags]
        public enum ParamFlags
        {
            None = 0,
            Buffer = 1,
            Optional = 2,
        }

        [Flags]
        public enum VirtualFlags
        {
            None = 0,
            Pure = 1,
            Const = 2,
            NoBase = 4,
            Final = 8,
        }

        public enum ParamMode
        {
            In,
            Out,
            Ref,
            Invalid,
        }

        /// <summary>
        /// One binding module. Only the fields the code generator reads are kept here; the
        /// extractor and the packaging scripts have their own registry.
        /// </summary>
        public sealed class ExtensionModule
        {
            public string Name;
            public string Namespace;
            public bool IsCore;
            public string NativeLibrary;
            /// <summary>The Qt include module (QtCharts, Qt3DCore, ...), used to name the free function class.</summary>
            public string QtModule;
            public string[] CppNamespaces = Array.Empty<string>();
            public string[] DependsOn = Array.Empty<string>();
        }
    }
}
