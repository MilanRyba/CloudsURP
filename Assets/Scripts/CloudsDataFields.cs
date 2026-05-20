using System;
using UnityEngine;

[Serializable]
public class GeneralCDFs
{
	[Range(2, 8)]
	public int CoverageRepeat = 4;

	public bool AnimateCoverage = false;

	[Range(0.01f, 1.0f)]
	public float CloudMinHeight = 0.3f;

	[Range(0.01f, 1.0f)]
	public float CloudMaxHeight = 0.75f;

	[Range(0.0f, 1.0f)]
	public float GlobalDensity = 0.026f;
}

[Serializable]
public class WindCDFs
{
	[Range(0.0f, 360.0f), Tooltip("Angle of the global wind direction")]
	public float WindAngle = 0.0f;

	[Range(0.0f, 100.0f), Tooltip("Speed of the clouds")]
	public float CloudSpeed = 0.0f;

	[Range(0.0f, 250.0f), Tooltip("Pushes the tops of the clouds along the wind direction by this many units")]
	public float CloudTopOffset = 100.0f;
}

[Serializable]
public class NoiseCDFs
{
	[Range(0.1f, 5.0f), Tooltip("Scale of the base cloud shape")]
	public float ShapeNoiseScale = 1.8f;

	[Range(0.1f, 5.0f), Tooltip("Scale of the cloud details")]
	public float DetailNoiseScale = 2.69f;

	[Range(0.0f, 1.0f)]
	public float DetailNoiseInfluence = 0.4f;

	[Range(0.0f, 50.0f), Tooltip("Controls the strength of atmospheric turbulence")]
	public float Curliness = 2.8f;
}

[CreateAssetMenu(fileName = "CDF", menuName = "Scriptable Objects/CDF")]
public class CloudsDataFields : ScriptableObject
{
	public Texture2D CloudMap;

	[Space(5)]
	public GeneralCDFs General;

	[Space(5)]
	public WindCDFs Wind;

	[Space(5)]
	public NoiseCDFs Noise;
}
