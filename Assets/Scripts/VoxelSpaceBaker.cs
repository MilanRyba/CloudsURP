using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class VoxelSpaceBaker : MonoBehaviour
{
    public Transform CloudEllipsoid;

	const int m_NumEllipsoids = 100;
    List<Transform> m_Ellipsoids = new List<Transform>();

	[Header("KEEP THE POSITION OF THIS GAME OBJECT THE SAME AS Space.WorldOffset")]

	public VoxelSpace Space;

	public void AddEllipsoid()
	{
        if (m_Ellipsoids.Count == m_NumEllipsoids)
            return;

        Transform t = Instantiate(CloudEllipsoid, transform);       
        m_Ellipsoids.Add(t);
	}

	private void OnDrawGizmos()
	{
		Vector3 origin = Space.WorldOffset;
		
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireCube(origin, Space.WorldExtents);

		Gizmos.color = new Color(0.2f, 0.7f, 1.0f, 0.1f);
	}

	public void Refresh()
	{
		m_Ellipsoids = new List<Transform>();
		int ellipsoidCounter = 0;

		foreach (Transform child in gameObject.transform)
		{
			if (ellipsoidCounter == m_NumEllipsoids)
			{
				Debug.LogWarning("Exceeded the maximum number of ellipsoids - 100");
				return;
			}

			child.name = $"Ellipsoid " + ellipsoidCounter;
			ellipsoidCounter++;

			m_Ellipsoids.Add(child);
		}
	}

	public void SaveAsCVDF()
	{
		CloudsVoxelDataFields CVDF = ScriptableObject.CreateInstance<CloudsVoxelDataFields>();
		CVDF.Space = Space;

		CVDF.Ellipsoids = new CloudsVoxelDataFields.Ellipsoid[m_Ellipsoids.Count];
		for (int i = 0; i < m_Ellipsoids.Count; i++)
		{
			Transform ellipsoid = m_Ellipsoids[i];
			CVDF.Ellipsoids[i] = new CloudsVoxelDataFields.Ellipsoid(ellipsoid.position, ellipsoid.localScale);
		}

		AssetDatabase.CreateAsset(CVDF, "Assets/CVDF/CVDF_FromCode.asset");
	}
}
