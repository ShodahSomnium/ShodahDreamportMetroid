#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class ShaderInspectorCustomVector3 : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
    {
        Vector4 v = prop.vectorValue;
        EditorGUI.BeginChangeCheck();
        Vector3 v3 = EditorGUI.Vector3Field(position, label, new Vector3(v.x, v.y, v.z));
        if (EditorGUI.EndChangeCheck())
        {
            prop.vectorValue = new Vector4(v3.x, v3.y, v3.z, 0);
        }
    }
}
#endif