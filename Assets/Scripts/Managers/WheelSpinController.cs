using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using System;
using TMPro;

public enum WheelSegmentType
{
    Multiplier,
    FreeGames
}

[Serializable]
public class WheelSegmentData
{
    public WheelSegmentType type;
    [Tooltip("Only for FreeGames type")] public string featureName; 
    public TextMeshProUGUI valueText;
    
    [HideInInspector] public double assignedValue;
    public int serverIndex = -1;
}

public class WheelSpinController : MonoBehaviour
{
    [Header("Wheel Configuration")]
    [SerializeField] private RectTransform wheelRect;
    [SerializeField] private RectTransform arrowRect;

    [Header("Segments (CLOCKWISE ORDER)")]
    [SerializeField] private List<WheelSegmentData> segments = new List<WheelSegmentData>();

    [Header("Angle Settings")]
    [Tooltip("Angle where segment[0] center is located. 90 = Top.")]
    [SerializeField] private float startOffsetAngle = 90f;

    [Header("Text Alignment")]
    [SerializeField] private float textRadius = 250f;
    [SerializeField] private bool faceOutward = true;

    [Header("Spin Settings")]
    [SerializeField] private float spinDuration = 5f;
    [SerializeField] private int extraSpins = 4;
    [SerializeField] private Ease spinEase = Ease.OutCubic;
    [Tooltip("Fine-tune the landing position. Positive shifts clockwise.")]
    [SerializeField] private float alignmentOffset = 0f;

    private float segmentAngle;
    private bool isSpinning;
    private int currentTargetIndex = -1;

    internal bool IsSpinning => isSpinning;
    internal List<WheelSegmentData> SegmentDataList => segments;

    private void Awake()
    {
        Initialize(segments.Count);
    }

    internal void Initialize(int segmentCount)
    {
        if (segmentCount <= 0) return;
        segmentAngle = 360f / segmentCount;
    }

    public void OverrideSegmentsWithData(List<USpinSegment> serverSegments)
    {
        if (serverSegments == null || segments == null) return;

        foreach (var serverSeg in serverSegments)
        {
            if (serverSeg.sliceIndex >= 0 && serverSeg.sliceIndex < segments.Count)
            {
                var localSeg = segments[serverSeg.sliceIndex];
                localSeg.serverIndex = serverSeg.sliceIndex;
                
                if (serverSeg.type == "MULTIPLIER")
                {
                    localSeg.type = WheelSegmentType.Multiplier;
                    localSeg.assignedValue = serverSeg.multiplier;
                }
                else if (serverSeg.type == "FREE_GAMES")
                {
                    localSeg.type = WheelSegmentType.FreeGames;
                    localSeg.assignedValue = serverSeg.freeGames;
                }
            }
        }

        // Re-align and re-format texts based on the new data
        SetupSegmentTexts();
    }

    [ContextMenu("Setup Segment Texts")]
    public void SetupSegmentTexts()
    {
        if (segments == null || segments.Count == 0) return;
        
        float angleStep = 360f / segments.Count;
        
        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.valueText == null) continue;
            
            // Format text
            if (segment.type == WheelSegmentType.Multiplier)
            {
                string valStr = "";
                if (segment.assignedValue > 0)
                {
                    valStr = segment.assignedValue.ToString() + "X";
                }
                else
                {
                    string currentText = segment.valueText.text != null ? segment.valueText.text.Replace("\n", "").Replace("X", "").Trim() : "";
                    if (string.IsNullOrEmpty(currentText) || currentText.Contains("Free") || currentText.Contains("<")) currentText = "20";
                    valStr = currentText + "X";
                }
                
                char[] chars = valStr.ToCharArray();
                segment.valueText.text = string.Join("\n", chars);
            }
            else if (segment.type == WheelSegmentType.FreeGames)
            {
                segment.valueText.text = "<size= 30>Free</size>\nG\nA\nM\nE\nS";
            }
            
            // Position & Rotation
            float angle = startOffsetAngle - (i * angleStep) - alignmentOffset;
            float rad = angle * Mathf.Deg2Rad;
            
            Vector3 localDir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0);
            segment.valueText.rectTransform.localPosition = localDir * textRadius;
            
            float zRot = angle - 90f;
            if (!faceOutward) zRot += 180f; // Face inward
            
            segment.valueText.rectTransform.localRotation = Quaternion.Euler(0, 0, zRot);
        }
    }

    internal void SpinToIndex(int targetIndex, Action onComplete = null)
    {
        if (isSpinning) return;
        StartCoroutine(SpinRoutine(targetIndex, onComplete));
    }

    private IEnumerator SpinRoutine(int targetIndex, Action onComplete)
    {
        isSpinning = true;
        currentTargetIndex = targetIndex;
        AudioManager.Instance?.PlayWheelStart();

        // 1. Calculate the angle for the target segment relative to the wheel's local zero
        float targetSegmentAngle = (targetIndex * segmentAngle) + alignmentOffset;
        float wheelLocalAngle = startOffsetAngle - targetSegmentAngle;
        
        // 2. Get the direction from the wheel center to the arrow
        // (arrow - wheel) gives the vector pointing toward the winning spot on the wheel's edge
        Transform parent = wheelRect.parent;
        Vector3 worldWinningDir = (arrowRect.position - wheelRect.position).normalized;
        Vector3 localWinningDir = parent != null ? parent.InverseTransformDirection(worldWinningDir) : worldWinningDir;
        
        if (localWinningDir.sqrMagnitude < 0.1f) localWinningDir = Vector3.right;
        
        float arrowAngle = Mathf.Atan2(localWinningDir.y, localWinningDir.x) * Mathf.Rad2Deg;
        
        // 3. The target local rotation for the wheel
        float finalTargetLocalRotation = arrowAngle - wheelLocalAngle;

        // 5. Calculate total rotation for Tweening
        float currentLocalRotation = wheelRect.localEulerAngles.z;
        
        // Find the absolute target rotation that is clockwise from current
        float targetAbs = finalTargetLocalRotation;
        while (targetAbs > currentLocalRotation) targetAbs -= 360f;
        
        // Add extra spins
        float totalRotation = targetAbs - (extraSpins * 360f);

        Debug.Log($"[WheelSpin] Target Index: {targetIndex}, Arrow Angle: {arrowAngle:F1}, Target Local Z: {finalTargetLocalRotation:F1}, Total Rotation: {totalRotation:F1}");

        float lastAngle = wheelRect.localEulerAngles.z;
        float accumulatedAngleChange = 0f;
        float anglePerSegment = (segments != null && segments.Count > 0) ? (360f / segments.Count) : 20f;

        wheelRect.DORotate(
            new Vector3(0, 0, totalRotation),
            spinDuration,
            RotateMode.FastBeyond360
        ).SetEase(spinEase).OnUpdate(() =>
        {
            float currentAngle = wheelRect.localEulerAngles.z;
            float delta = Mathf.DeltaAngle(lastAngle, currentAngle);
            accumulatedAngleChange += Mathf.Abs(delta);
            if (accumulatedAngleChange >= anglePerSegment)
            {
                accumulatedAngleChange %= anglePerSegment;
                AudioManager.Instance?.PlayWheelSegmentTick();
            }
            lastAngle = currentAngle;
        });

        yield return new WaitForSeconds(spinDuration);

        // Snap to perfect alignment
        wheelRect.localRotation = Quaternion.Euler(0, 0, finalTargetLocalRotation);

        isSpinning = false;
        onComplete?.Invoke();
    }



    private void OnDrawGizmos()
    {
        DrawWheelGizmos(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawWheelGizmos(true);
    }

    private void DrawWheelGizmos(bool selected)
    {
        if (wheelRect == null) return;
        
        int count = (segments != null && segments.Count > 0) ? segments.Count : 18; // Default to 18 if list is empty
        float angleStep = 360f / count;
        Vector3 center = wheelRect.position;
        
        // Calculate radius based on RectTransform size and scale
        float radius = (wheelRect.rect.width > 0) ? (wheelRect.rect.width * 0.5f * wheelRect.lossyScale.x) : 100f;

        Gizmos.color = selected ? Color.white : new Color(1, 1, 1, 0.2f);
        Gizmos.DrawWireSphere(center, radius * 0.05f);

        for (int i = 0; i < count; i++)
        {
            float angle = startOffsetAngle - (i * angleStep) - alignmentOffset;
            float rad = angle * Mathf.Deg2Rad;
            // Local direction
            Vector3 localDir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0);
            // World direction
            Vector3 worldDir = wheelRect.rotation * localDir;
            
            if (selected)
            {
                if (currentTargetIndex != -1 && i == currentTargetIndex)
                    Gizmos.color = Color.magenta; // Winning segment
                else
                    Gizmos.color = (i == 0) ? Color.green : Color.red;

                Gizmos.DrawLine(center, center + worldDir * radius);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(center + worldDir * radius * 1.05f, i.ToString());
#endif
            }
            else
            {
                Gizmos.color = new Color(1, 1, 1, 0.1f);
                Gizmos.DrawLine(center, center + worldDir * radius * 0.5f);
            }
        }

        if (arrowRect != null)
        {
            Gizmos.color = selected ? Color.yellow : new Color(1, 0.92f, 0.016f, 0.3f);
            Vector3 arrowPos = arrowRect.position;
            // Direction from wheel center to arrow (World Space for Gizmos)
            Vector3 worldWinningDir = (arrowPos - wheelRect.position).normalized;
            Vector3 endPos = wheelRect.position + worldWinningDir * radius;
            
            // Draw line from center to arrow position on edge
            Gizmos.DrawLine(wheelRect.position, endPos);
            
            if (selected)
            {
                // Draw arrow head
                float headSize = radius * 0.05f;
                Vector3 right = Vector3.Cross(worldWinningDir, Vector3.forward).normalized;
                Vector3 headLeft = endPos - worldWinningDir * headSize + right * headSize * 0.5f;
                Vector3 headRight = endPos - worldWinningDir * headSize - right * headSize * 0.5f;
                
                Gizmos.DrawLine(endPos, headLeft);
                Gizmos.DrawLine(endPos, headRight);
                Gizmos.DrawLine(headLeft, headRight);
            }
        }
    }
}
