namespace QtSharp.Generator
{
    /// <summary>
    /// The repeating C# shapes of the container helpers. The body is a template; the element
    /// codec and the names stay in the emitter.
    /// </summary>
    public static class CSharpTemplates
    {
        /// <summary>
        /// The read side: ReadTransient does the work, Read also releases a container Qt allocated
        /// for the return value.
        /// </summary>
        public static string ReadWrapper(string managedType, string variable, string freeExpression) => $$"""
            public static {{managedType}} Read(IntPtr {{variable}})
            {
                var result = ReadTransient({{variable}});
                if ({{variable}} != IntPtr.Zero)
                    {{freeExpression}};
                return result;
            }
            """;

        /// <summary>The write side: build the native value, copy it into the caller's slot, release it.</summary>
        public static string WriteWrapper(
            string managedType, string variable, string parameter,
            string newExpression, string assignExpression) => $$"""
            internal static void Write(IntPtr destination, {{managedType}} {{parameter}})
            {
                var {{variable}} = {{newExpression}};
                try
                {
                    {{assignExpression}};
                }
                finally
                {
                    Free({{variable}});
                }
            }
            """;
    }
}
