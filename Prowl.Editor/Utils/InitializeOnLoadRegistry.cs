using System;

namespace Infinity.Editor.Core;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class InitializeOnLoadAttribute : Attribute { }
