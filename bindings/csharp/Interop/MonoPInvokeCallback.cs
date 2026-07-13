using System;

namespace GameNetworkingSockets
{
    /// <summary>
    /// Marks a static method that is passed to native code as a function pointer.
    /// Unity's IL2CPP AOT compiler requires every such method to carry an attribute
    /// literally named "MonoPInvokeCallbackAttribute" — it matches by name, not by
    /// assembly, so this local copy works without a UnityEngine reference (keeping the
    /// wrapper engine-agnostic for desktop). On Mono/.NET it is an inert marker.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type delegateType) => DelegateType = delegateType;

        public Type DelegateType { get; }
    }
}
