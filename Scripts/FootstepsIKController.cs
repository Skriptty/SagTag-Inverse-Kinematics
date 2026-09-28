using UnityEngine;
using GameNetcodeStuff;

[DefaultExecutionOrder(10000)]
public class FootIKController : MonoBehaviour
{
    [Header("Floor")]
    public float maxStepUp = 0.35f;
    public float maxStepDown = 0.45f;

    [Header("Calibration")]
    public float planeOffset = 0f;

    [Header("Foot  Adjusting (adjust at own risk)")]
    public float plantStart = 0.15f;
    public float plantEnd = 0.40f;

    [Header("Smoothing")]
    public float footSmoothSpeed = 20f;
    public float hipSmoothSpeed = 12f;
    public float weightFadeSpeed = 6f;
    public float maxFootTilt = 35f;
    public float maxHipDrop = 0.3f;

    [Header("Debug")]
    public bool debugLog = false;
    public bool debugMarkers = false; 

    private struct FootSample
    {
        public float delta;
        public float plant;
        public Quaternion tilt;
        public bool hit;
        public Vector3 hitPoint;
        public string hitInfo;
    }

    private bool initialized;
    private int initAttempts;
    private int lastFrame = -1;
    private float logTimer;

    private LayerMask groundMask;
    private Animator animator;
    private PlayerControllerB controller;
    private Transform bodyT; 
    private Transform hips, lUpper, lLower, lFoot, rUpper, rLower, rFoot;
    private string boneSource = "?";

    private float weight;
    private float lDelta, rDelta, hipOffset, hover;
    private Quaternion lTilt = Quaternion.identity;
    private Quaternion rTilt = Quaternion.identity;

    private Transform mPlane, mLHit, mLTarget, mRHit, mRTarget;

    void Awake()
    {
        if (FootIKPlugin.Log != null)
            FootIKPlugin.Log.LogInfo("[FootIK] IK Script attached");
    }

    void OnEnable()
    {
        Application.onBeforeRender += OnBeforeRender;
    }

    void OnDisable()
    {
        Application.onBeforeRender -= OnBeforeRender;
        HideMarkers();
    }

    void OnDestroy()
    {
        DestroyMarkers();
    }

    void Update()
    {
        if (!initialized) TryInitialize();
    }
    

    void TryInitialize()
    {
        if (controller == null) controller = GetComponentInParent<PlayerControllerB>();
        if (animator == null) animator = GetComponentInParent<Animator>(); 

        if (controller == null || animator == null || StartOfRound.Instance == null) return;

        initAttempts++;
        if (initAttempts > 600)
        {
            FootIKPlugin.Log.LogError($"[FootIK] Couldnt map out bones in {controller.playerUsername}, how is that player missing bones??:");
            LogMissingBones();
            enabled = false;
            return;
        }
        
        groundMask = StartOfRound.Instance.allPlayersCollideWithMask;
        if (groundMask.value == 0) return;

        if (!TryFindBones()) return;

        if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        bodyT = controller.thisPlayerBody != null ? controller.thisPlayerBody : controller.transform;
        initialized = true;

        bool chainOk = lFoot.parent == lLower && lLower.parent == lUpper
                    && rFoot.parent == rLower && rLower.parent == rUpper;
        float lx = bodyT.InverseTransformPoint(lFoot.position).x;
        float rx = bodyT.InverseTransformPoint(rFoot.position).x;
        
    }

    bool TryFindBones()
    {
        if (animator.isHuman)
        {
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            lUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            lLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (HaveAllBones()) { boneSource = "Humanoid"; return true; }
        }

        Transform searchRoot = animator.transform;
        hips = FindBone(searchRoot, "spine", "pelvis", "hips");
        lUpper = FindBone(searchRoot, "thigh_L", "thigh.L");
        lLower = FindBone(searchRoot, "shin_L", "shin.L");
        lFoot = FindBone(searchRoot, "foot_L", "foot.L");
        rUpper = FindBone(searchRoot, "thigh_R", "thigh.R");
        rLower = FindBone(searchRoot, "shin_R", "shin.R");
        rFoot = FindBone(searchRoot, "foot_R", "foot.R");
        if (HaveAllBones()) { boneSource = "Por nombre"; return true; }

        return false;
    }

    bool HaveAllBones()
    {
        return hips != null && lUpper != null && lLower != null && lFoot != null
            && rUpper != null && rLower != null && rFoot != null;
    }

    private Transform FindBone(Transform root, params string[] names)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        foreach (Transform t in all)
            foreach (string name in names)
                if (t.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    return t;

        foreach (Transform t in all)
            foreach (string name in names)
                if (t.name.EndsWith(name, System.StringComparison.OrdinalIgnoreCase))
                    return t;

        return null;
    }

    private void LogMissingBones()
    {
        if (hips == null) FootIKPlugin.Log.LogError(" -> Missing spine");
        if (lUpper == null) FootIKPlugin.Log.LogError(" -> Missing thigh_L");
        if (lLower == null) FootIKPlugin.Log.LogError(" -> Missing shin_L");
        if (lFoot == null) FootIKPlugin.Log.LogError(" -> Missing foot_L");
        if (rUpper == null) FootIKPlugin.Log.LogError(" -> Missing thigh_R");
        if (rLower == null) FootIKPlugin.Log.LogError(" -> Missing shin_R");
        if (rFoot == null) FootIKPlugin.Log.LogError(" -> Missing foot_R");
    }

    static string LayerNames(int mask)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0)
                sb.Append(i).Append(':').Append(LayerMask.LayerToName(i)).Append("  ");
        return sb.ToString();
    }
    

    void OnBeforeRender()
    {
        if (!initialized) return;
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        UpdateLegs();
    }

    void ResetState()
    {
        lDelta = rDelta = hipOffset = hover = 0f;
        lTilt = rTilt = Quaternion.identity;
    }

    void UpdateLegs()
    {
        bool shouldApply = animator.enabled
            && animator.runtimeAnimatorController != null
            && !controller.isPlayerDead
            && !controller.performingEmote
            && !controller.inSpecialInteractAnimation
            && controller.inAnimationWithEnemy == null
            && !controller.isClimbingLadder
            && !controller.isUnderwater;

        weight = Mathf.MoveTowards(weight, shouldApply ? 1f : 0f, Time.deltaTime * weightFadeSpeed);
        if (weight <= 0.001f)
        {
            ResetState();
            HideMarkers();
            return;
        }

        Transform root = controller.transform;
        
        Vector3 lAnimPos = lFoot.position;
        Vector3 rAnimPos = rFoot.position;
        Quaternion lAnimRot = lFoot.rotation;
        Quaternion rAnimRot = rFoot.rotation;
        
        float planeY = animator.transform.position.y + planeOffset;
        
        bool rootGround = Physics.Raycast(root.position + Vector3.up * 0.6f, Vector3.down, out RaycastHit rootHit, 2f, groundMask, QueryTriggerInteraction.Ignore);
        float groundRootY = rootGround ? rootHit.point.y : root.position.y;
        
        float hoverRaw = planeY - groundRootY;
        float hoverWant = (rootGround && hoverRaw > -0.1f && hoverRaw < 0.15f) ? hoverRaw : 0f;
        
        FootSample ls = SampleFoot(lAnimPos, planeY, groundRootY);
        FootSample rs = SampleFoot(rAnimPos, planeY, groundRootY);
        if (!rootGround)
        {
            ls.delta = rs.delta = 0f;
            ls.tilt = rs.tilt = Quaternion.identity;
        }
        
        float lWant = (ls.delta >= 0f ? ls.delta : ls.delta * ls.plant) * weight;
        float rWant = (rs.delta >= 0f ? rs.delta : rs.delta * rs.plant) * weight;

        float k = 1f - Mathf.Exp(-footSmoothSpeed * Time.deltaTime);
        float hk = 1f - Mathf.Exp(-hipSmoothSpeed * Time.deltaTime);

        lDelta = Mathf.Lerp(lDelta, lWant, k);
        rDelta = Mathf.Lerp(rDelta, rWant, k);
        lTilt = Quaternion.Slerp(lTilt, Quaternion.Slerp(Quaternion.identity, ls.tilt, ls.plant * weight), k);
        rTilt = Quaternion.Slerp(rTilt, Quaternion.Slerp(Quaternion.identity, rs.tilt, rs.plant * weight), k);

        hover = Mathf.Lerp(hover, hoverWant * weight, hk);
        
        float hipWant = Mathf.Clamp(Mathf.Min(0f, Mathf.Min(lDelta, rDelta)), -maxHipDrop, 0f);
        hipOffset = Mathf.Lerp(hipOffset, hipWant, hk);

        if (debugMarkers)
            UpdateMarkers(root.position, planeY, ls, rs, lAnimPos + Vector3.up * (lDelta - hover), rAnimPos + Vector3.up * (rDelta - hover));

        if (debugLog)
        {
            logTimer -= Time.deltaTime;
            if (logTimer <= 0f)
            {
                logTimer = 2f;
                string rootInfo = rootGround ? $"{rootHit.collider.name}[{LayerMask.LayerToName(rootHit.collider.gameObject.layer)}]" : "";
            }
        }

        if (Mathf.Abs(lDelta) < 0.0005f && Mathf.Abs(rDelta) < 0.0005f && Mathf.Abs(hipOffset) < 0.0005f && Mathf.Abs(hover) < 0.0005f) return;
        
        hips.position += Vector3.up * (hipOffset - hover);

        SolveLeg(lUpper, lLower, lFoot, lAnimPos + Vector3.up * (lDelta - hover), lAnimRot, lTilt);
        SolveLeg(rUpper, rLower, rFoot, rAnimPos + Vector3.up * (rDelta - hover), rAnimRot, rTilt);
    }

    FootSample SampleFoot(Vector3 animFootPos, float planeY, float groundRootY)
    {
        FootSample s = new FootSample { tilt = Quaternion.identity, hitInfo = "" };

        float heightAbovePlane = animFootPos.y - planeY;
        s.plant = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(plantStart, plantEnd, heightAbovePlane));

        float startAbove = maxStepUp + 0.25f;
        Vector3 origin = new Vector3(animFootPos.x, groundRootY + startAbove, animFootPos.z);
        float rayLen = startAbove + maxStepDown + 0.2f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayLen, groundMask, QueryTriggerInteraction.Ignore))
        {
            s.hit = true;
            s.hitPoint = hit.point;
            s.hitInfo = $"{hit.collider.name}[{LayerMask.LayerToName(hit.collider.gameObject.layer)}] y={hit.point.y:F2}";

            float diff = hit.point.y - groundRootY;
            if (diff >= -maxStepDown && diff <= maxStepUp)
            {
                s.delta = diff;
                Quaternion raw = Quaternion.FromToRotation(Vector3.up, hit.normal);
                s.tilt = Quaternion.RotateTowards(Quaternion.identity, raw, maxFootTilt);
            }
            else
            {
                s.hitInfo += "";
            }
        }

        return s;
    }

    void SolveLeg(Transform upper, Transform lower, Transform foot, Vector3 targetPos, Quaternion animFootRot, Quaternion tilt)
    {
        Vector3 a = upper.position;
        Vector3 b = lower.position;
        Vector3 c = foot.position;

        float lenUpper = (b - a).magnitude;
        float lenLower = (c - b).magnitude;
        if (lenUpper < 1e-4f || lenLower < 1e-4f) return;

        Vector3 toTarget = targetPos - a;
        float dist = toTarget.magnitude;
        if (dist < 1e-4f) return;

        float maxReach = (lenUpper + lenLower) * 0.999f;
        float minReach = Mathf.Abs(lenUpper - lenLower) * 1.001f + 0.001f;
        float clampedDist = Mathf.Clamp(dist, minReach, maxReach);
        Vector3 dir = toTarget / dist;
        Vector3 clampedTarget = a + dir * clampedDist;
        
        Vector3 acDir = (c - a).normalized;
        Vector3 kneeOffset = (b - a) - acDir * Vector3.Dot(b - a, acDir);
        Vector3 bend = kneeOffset.sqrMagnitude > 1e-6f ? kneeOffset.normalized : bodyT.forward;
        Vector3 ortho = bend - dir * Vector3.Dot(bend, dir);
        if (ortho.sqrMagnitude < 1e-6f) ortho = Vector3.Cross(dir, bodyT.right);
        bend = ortho.normalized;

        float cosA = Mathf.Clamp((lenUpper * lenUpper + clampedDist * clampedDist - lenLower * lenLower) / (2f * lenUpper * clampedDist), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 newKnee = a + dir * (lenUpper * cosA) + bend * (lenUpper * sinA);

        upper.rotation = Quaternion.FromToRotation(b - a, newKnee - a) * upper.rotation;

        Vector3 kneeNow = lower.position;
        lower.rotation = Quaternion.FromToRotation(foot.position - kneeNow, clampedTarget - kneeNow) * lower.rotation;

        foot.rotation = tilt * animFootRot;
    }
    

    Transform CreateMarker(Color color, float size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * size;

        Renderer r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Material src = controller.thisPlayerModel != null ? controller.thisPlayerModel.sharedMaterial : r.sharedMaterial;
        Material mat = new Material(src);
        if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", null);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        r.material = mat;

        return go.transform;
    }

    void UpdateMarkers(Vector3 rootPos, float planeY, FootSample ls, FootSample rs, Vector3 lTarget, Vector3 rTarget)
    {
        if (mPlane == null)
        {
            mPlane = CreateMarker(Color.white, 0.10f);                 
            mLHit = CreateMarker(new Color(0.1f, 0.3f, 1f), 0.07f);     
            mLTarget = CreateMarker(Color.cyan, 0.09f);                 
            mRHit = CreateMarker(new Color(1f, 0.1f, 0.1f), 0.07f);     
            mRTarget = CreateMarker(new Color(1f, 0.6f, 0f), 0.09f);    
        }

        mPlane.gameObject.SetActive(true);
        mPlane.position = new Vector3(rootPos.x, planeY, rootPos.z);

        mLHit.gameObject.SetActive(ls.hit);
        mLHit.position = ls.hitPoint;
        mRHit.gameObject.SetActive(rs.hit);
        mRHit.position = rs.hitPoint;

        mLTarget.gameObject.SetActive(true);
        mLTarget.position = lTarget;
        mRTarget.gameObject.SetActive(true);
        mRTarget.position = rTarget;
    }

    void HideMarkers()
    {
        if (mPlane != null) mPlane.gameObject.SetActive(false);
        if (mLHit != null) mLHit.gameObject.SetActive(false);
        if (mLTarget != null) mLTarget.gameObject.SetActive(false);
        if (mRHit != null) mRHit.gameObject.SetActive(false);
        if (mRTarget != null) mRTarget.gameObject.SetActive(false);
    }

    void DestroyMarkers()
    {
        if (mPlane != null) Destroy(mPlane.gameObject);
        if (mLHit != null) Destroy(mLHit.gameObject);
        if (mLTarget != null) Destroy(mLTarget.gameObject);
        if (mRHit != null) Destroy(mRHit.gameObject);
        if (mRTarget != null) Destroy(mRTarget.gameObject);
    }
}