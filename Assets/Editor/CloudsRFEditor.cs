using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CloudsRendererFeature))]
public class CloudsRFEditor : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();

		CloudsRendererFeature rf = (CloudsRendererFeature)target;

		if (GUILayout.Button("Save Current Cloud Map"))
		{
			rf.m_CloudsPass.SaveCloudMapAsAsset();
		}

		if (GUILayout.Button("Save As CDF"))
		{
			rf.m_CloudsPass.SaveCDF();
		}
	}
}
