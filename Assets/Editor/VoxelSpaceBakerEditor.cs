using UnityEngine;
using UnityEditor;
using System;

[CustomEditor(typeof(VoxelSpaceBaker))]
public class VoxelSpaceBakerEditor : Editor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();

		VoxelSpaceBaker baker = (VoxelSpaceBaker)target;

		if (GUILayout.Button("Add Ellipsoid"))
		{
			baker.AddEllipsoid();
		}

		if (GUILayout.Button("Refresh"))
		{
			baker.Refresh();
		}

		if (GUILayout.Button("Save As Voxel Space"))
		{
			baker.SaveAsCVDF();
		}
	}
}
