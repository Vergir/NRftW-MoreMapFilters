using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppQuantum;

namespace MapMarkers;

internal static class Tags
{
    /// <summary>
    /// MarkupTag ints of a MarkupTagDataCondition list. The lists are il2cpp MarkupTagData[] (MarkupTagData has a reference
    /// field, so the interop has no struct array for it): read the array directly. Layout: max_length at +0x18, elements from
    /// +0x20, MarkupTagData = { int Tag @0, bool NoEdit @4, ref @8 } = 16 bytes. A List&lt;MarkupTagData&gt; is handled too.
    /// </summary>
    public static void Read(Il2CppSystem.Collections.Generic.IReadOnlyList<MarkupTagData>? list, List<int> into)
    {
        if (list == null) return;
        var asList = list.TryCast<Il2CppSystem.Collections.Generic.List<MarkupTagData>>();
        if (asList != null)
        {
            for (int i = 0; i < asList.Count; i++) into.Add((int)asList[i].Tag);
            return;
        }
        IntPtr arr = list.Pointer;
        // Only read raw memory from objects whose il2cpp class is an array (checked once per class pointer).
        IntPtr klass = Marshal.ReadIntPtr(arr, 0);
        if (!ArrayClasses.Contains(klass))
        {
            if (!list.Cast<Il2CppSystem.Object>().GetIl2CppType().FullName.EndsWith("[]")) return;
            ArrayClasses.Add(klass);
        }
        long n = Marshal.ReadInt64(arr, 0x18);
        for (int i = 0; i < n; i++) into.Add(Marshal.ReadInt32(arr, 0x20 + i * 16));
    }

    private static readonly HashSet<IntPtr> ArrayClasses = new();
}
