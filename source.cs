using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KerbalSoaring
{
    [KSPAddon(KSPAddon.Startup.Flight, once: false)]
    public class KerbalSoaring : MonoBehaviour
    {
        // --- Inner Class for Thermal Data ---
        public class ThermalData
        {
            public double lat;
            public double lon;
            public float radius;
            public float height;
            public float intensity;
        }

        // --- Configuration ---
        private List<ThermalData> thermals = new List<ThermalData>();

        // Settings 
        private static bool debugMode = false;
        private static float rampUpAltitude = 250.0f;
        private const float SOLAR_CURVE_SCALE = 1.3f; // Scales how quickly solar factor ramps up

        // --- State ---
        private ThermalData currentThermal = null; // The thermal the ActiveVessel is currently in
        private float currentSolarFactor = 1.0f;
        private float currentAltFactor = 0.0f;

        // UI State
        private float displayWindSpeed = 0f;
        private float displayRadialFactor = 0f;
        private float msgTimer = 0f;
        private const float MSG_INTERVAL = 2f;

        // FAR Integration State
        private bool farHooked = false;
        private MethodInfo setWindMethod = null;

        // OPTIMIZATION: Use specific Func type instead of generic Delegate to avoid DynamicInvoke overhead
        private Func<CelestialBody, Part, Vector3, Vector3> otherWindModDelegate = null;

        // Cache
        private CelestialBody sunBody;

        public void Start()
        {
            LoadConfiguration();
            sunBody = Planetarium.fetch.Sun;

            // Start the hook routine
            StartCoroutine(HookRoutine());
        }

        private IEnumerator HookRoutine()
        {
            // Wait 1 second to ensure other wind mods (e.g. Kerbal Wind) initialize first
            yield return new WaitForSeconds(1.0f);

            farHooked = HookFarAtmosphere();
            if (farHooked)
            {
                string extraMsg = "";

                // Check if we found another wind mod
                if (otherWindModDelegate != null)
                {
                    // Get info for logging
                    string className = otherWindModDelegate.Method.DeclaringType.Name;
                    string methodName = otherWindModDelegate.Method.Name;

                    extraMsg = $" (Chained with: {className}.{methodName})";
                }

                Debug.Log($"[KerbalSoaring] Ready. Loaded {thermals.Count} thermals.{extraMsg}");
            }
            else
            {
                Debug.LogError("[KerbalSoaring] Critical Error: Could not hook FARAtmosphere. Soaring disabled.");
            }
        }

        public void OnDestroy()
        {
            if (farHooked && setWindMethod != null)
            {
                try
                {
                    // Restore the original wind provider (if any) when we leave the scene
                    setWindMethod.Invoke(null, new object[] { otherWindModDelegate });
                }
                catch { }
            }
        }

        public void Update()
        {
            if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null || !farHooked) return;

            // UI Feedback Loop
            msgTimer += Time.deltaTime;
            if (msgTimer >= MSG_INTERVAL)
            {
                msgTimer = 0f;
                // Only display if there is a thermal and we are getting significant lift
                if (currentThermal != null && displayWindSpeed > 0.25f)
                {
                    string status = debugMode
                        ? $"Updraft: {displayWindSpeed:F1} m/s | Centering Factor: {displayRadialFactor:P0}"
                        : $"Soaring...";

                    ScreenMessages.PostScreenMessage(status, 1.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        public void FixedUpdate()
        {
            if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null) return;
            Vessel v = FlightGlobals.ActiveVessel;

            // 1. Solar Factor
            if (sunBody != null)
            {
                Vector3 toSun = (sunBody.position - v.GetWorldPos3D()).normalized;
                Vector3 upVector = (v.GetWorldPos3D() - v.mainBody.position).normalized;
                currentSolarFactor = Mathf.Clamp01(Vector3.Dot(upVector, toSun) * SOLAR_CURVE_SCALE);
            }

            // 2. Altitude Ramp
            currentAltFactor = Mathf.Clamp01((float)v.radarAltitude / rampUpAltitude);

            // 3. Find Active Thermal (The heavy math)
            currentThermal = FindThermalForVessel(v);

            // Clear display if no thermal
            if (currentThermal == null) displayWindSpeed = 0f;
        }

        private ThermalData FindThermalForVessel(Vessel v)
        {
            if (v.mainBody == null || !v.mainBody.atmosphere) return null;
            if (v.altitude > 30000) return null; // assume no thermals above 30km

            Vector3d vesselPos = v.GetWorldPos3D();

            // OPTIMIZATION: Cache vessel Lat/Lon once per frame
            double vLat = v.latitude;
            double vLon = v.longitude;

            foreach (var t in thermals)
            {
                if (v.altitude > t.height) continue;

                // OPTIMIZATION: "Bounding Box" check.
                // Avoid doing heavy vector math if the thermal is further than 4 degrees away in lat/lon
                if (Math.Abs(vLat - t.lat) > 4.0 || Math.Abs(vLon - t.lon) > 4.0) continue;

                // Heavy Math: Coordinate conversion
                Vector3d thermalPos = v.mainBody.GetWorldSurfacePosition(t.lat, t.lon, v.altitude);
                if ((vesselPos - thermalPos).sqrMagnitude < (t.radius * t.radius))
                {
                    return t;
                }
            }
            return null;
        }

        // --- FAR ATMOSPHERE INTEGRATION ---

        private Vector3 GetWindVector(CelestialBody body, Part part, Vector3 position)
        {
            // --- Guard Clauses ---
            if (part == null || body == null) return Vector3.zero;

            Vector3 finalWind = Vector3.zero;

            // 1. Calculate OUR Wind
            // Ensure FlightGlobals is ready, ActiveVessel exists, and part.vessel is valid
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            bool isValidTarget = activeVessel != null && part.vessel != null && part.vessel == activeVessel;

            if (isValidTarget && currentThermal != null)
            {
                finalWind = CalculateSoaringWind(body, position, currentThermal);
            }

            // 2. Chain THEIR Wind (Kerbal Wind)
            // OPTIMIZATION: Direct delegate call instead of DynamicInvoke
            if (otherWindModDelegate != null)
            {
                try
                {
                    finalWind += otherWindModDelegate(body, part, position);
                }
                catch (Exception e)
                {
                    if (debugMode) Debug.LogWarning("[KerbalSoaring] Chained delegate error: " + e.Message);
                }
            }

            return finalWind;
        }

        private Vector3 CalculateSoaringWind(CelestialBody body, Vector3 position, ThermalData thermal)
        {
            // 1. Calculate Bell Curve (Radial Falloff)
            // We need the thermal center at the PART'S altitude to get an accurate cylinder
            double partAlt = body.GetAltitude(position);
            Vector3d tCenter = body.GetWorldSurfacePosition(thermal.lat, thermal.lon, partAlt);

            float dist = Vector3.Distance(position, tCenter);

            // Optimization: If wingtip is outside radius, return 0
            if (dist >= thermal.radius) return Vector3.zero;

            float rFactor = 0.5f * (1.0f + Mathf.Cos((dist / thermal.radius) * Mathf.PI));

            // 2. Magnitude
            float windSpeed = thermal.intensity * currentSolarFactor * currentAltFactor * rFactor;

            // 3. Update UI (only for root part to avoid flickering)
            if (Mathf.Abs(windSpeed) > 0.01f)
            {
                displayWindSpeed = windSpeed;
                displayRadialFactor = rFactor;
            }

            // 4. Direction (Up)
            Vector3 upVector = (position - body.position).normalized;
            return upVector * windSpeed;
        }

        private bool HookFarAtmosphere()
        {
            try
            {
                // Find FARAtmosphere
                Type farAtmType = null;
                foreach (var assembly in AssemblyLoader.loadedAssemblies)
                {
                    if (assembly.name.Contains("FerramAerospaceResearch"))
                    {
                        farAtmType = assembly.assembly.GetType("FerramAerospaceResearch.FARAtmosphere");
                        if (farAtmType != null) break;
                    }
                }
                if (farAtmType == null) return false;

                // Find SetWindFunction
                setWindMethod = farAtmType.GetMethod("SetWindFunction", BindingFlags.Public | BindingFlags.Static);
                if (setWindMethod == null) return false;

                // Check for existing delegate
                FieldInfo windField = farAtmType.GetField("windFunction", BindingFlags.NonPublic | BindingFlags.Static);
                if (windField != null)
                {
                    object dispatcher = windField.GetValue(null);
                    if (dispatcher != null)
                    {
                        PropertyInfo funcProp = dispatcher.GetType().GetProperty("Function");
                        if (funcProp != null)
                        {
                            Delegate existing = funcProp.GetValue(dispatcher, null) as Delegate;
                            // OPTIMIZATION: Attempt to cast directly to the Func type we need
                            if (existing != null)
                            {
                                otherWindModDelegate = existing as Func<CelestialBody, Part, Vector3, Vector3>;
                            }
                        }
                    }
                }

                // Hook our method
                MethodInfo myMethod = this.GetType().GetMethod("GetWindVector", BindingFlags.NonPublic | BindingFlags.Instance);
                Type funcType = typeof(Func<CelestialBody, Part, Vector3, Vector3>);
                Delegate myDelegate = Delegate.CreateDelegate(funcType, this, myMethod);

                setWindMethod.Invoke(null, new object[] { myDelegate });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[KerbalSoaring] Hook Error: " + e.Message);
                return false;
            }
        }

        private void LoadConfiguration()
        {
            thermals.Clear();
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("KERBALSOARING");
            if (nodes.Length == 0)
            {
                // Default fallback thermal just east of KSC runway
                thermals.Add(new ThermalData { lat = -0, lon = -74, radius = 2500, height = 4000, intensity = 10.0f });
                return;
            }
            foreach (ConfigNode node in nodes)
            {
                if (node.HasValue("debugMode")) bool.TryParse(node.GetValue("debugMode"), out debugMode);
                if (node.HasValue("rampUpAltitude")) float.TryParse(node.GetValue("rampUpAltitude"), out rampUpAltitude);

                foreach (ConfigNode thermalNode in node.GetNodes("THERMAL"))
                {
                    ThermalData t = new ThermalData();
                    double.TryParse(thermalNode.GetValue("lat"), out t.lat);
                    double.TryParse(thermalNode.GetValue("lon"), out t.lon);
                    float.TryParse(thermalNode.GetValue("radius"), out t.radius);
                    float.TryParse(thermalNode.GetValue("height"), out t.height);
                    float.TryParse(thermalNode.GetValue("intensity"), out t.intensity);
                    thermals.Add(t);
                }
            }
        }
    }
}