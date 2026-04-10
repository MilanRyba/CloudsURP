using UnityEngine;
using UnityEngine.InputSystem;

public class VoxelSpaceVisualizer : MonoBehaviour
{
	public VoxelSpace Space;
	public Mesh EllipsoidMesh;

	private bool m_VisualizeInPlayMode = true;

	private void Update()
	{
		if (Keyboard.current.vKey.wasPressedThisFrame)
			m_VisualizeInPlayMode = !m_VisualizeInPlayMode;
	}

	private void OnDrawGizmos()
	{
		if (Space == null)
			return;

		if (Application.isPlaying && m_VisualizeInPlayMode == false)
			return;

		Vector3 origin = Space.WorldOffset;

		Gizmos.color = Color.yellow;
		Gizmos.DrawWireCube(origin, Space.WorldExtents);

		Gizmos.color = new Color(0.2f, 0.7f, 1.0f, 0.1f);
		foreach (var e in Space.Ellipsoids)
			Gizmos.DrawWireMesh(EllipsoidMesh, e.Position, Quaternion.identity, e.Scale);
	}
}
