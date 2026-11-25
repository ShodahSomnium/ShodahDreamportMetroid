#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class ShaderInspectorCustomVector2 : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
    {
        Vector4 v = prop.vectorValue;
        EditorGUI.BeginChangeCheck();
        Vector2 v2 = EditorGUI.Vector2Field(position, label, new Vector2(v.x, v.y));
        if (EditorGUI.EndChangeCheck())
        {
            prop.vectorValue = new Vector4(v2.x, v2.y, v.z, v.w);
        }
    }
}
#endif