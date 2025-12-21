using System;
using System.Collections.Generic;
using UnityEngine;
using WindAPI; // Reference the new namespace

namespace KerbalSoaring
{
    [KSPAddon(KSPAddon.Startup.Flight, once: false)]
    // Implement IWindProvider interface
    public class KerbalSoaring : MonoBehaviour, IWindProvider
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
        private static bool debugMode = false;
        private static float rampUpAltitude = 250.0f;
        private const float SOLAR_CURVE_SCALE = 1.3f;

        // --- State ---
        private ThermalData currentThermal = null;
        private float currentSolarFactor = 1.0f;
        private float currentAltFactor = 0.0f;

        // UI State
        private float displayWindSpeed = 0f;
        private float displayRadialFactor = 0f;
        private float msgTimer = 0f;
        private const float MSG_INTERVAL = 2f;

        private CelestialBody sunBody;

        // --- IWindProvider Implementation ---

        public string ProviderID => "KerbalSoaring";

        public Vector3 GetWind(CelestialBody body, Part part, Vector3 position)
        {
            // Guard clauses
            if (part == null || body == null) return Vector3.zero;

            // Only apply wind to the Active Vessel (optimization)
            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel == null || part.vessel != activeVessel) return Vector3.zero;

            // If we are in a thermal, calculate the vector
            if (currentThermal != null)
            {
                return CalculateSoaringWind(body, position, currentThermal);
            }

            return Vector3.zero;
        }

        // --- Lifecycle ---

        public void Start()
        {
            LoadConfiguration();
            sunBody = Planetarium.fetch.Sun;

            // REGISTER with WindAPI
            if (WindManager.Instance != null)
            {
                WindManager.Instance.RegisterProvider(this);
            }
            else
            {
                // Fallback if WindAPI loads slightly later, retry in a coroutine normally
                // But for simplicity here, we assume WindAPI loads fast.
                Debug.LogWarning("[KerbalSoaring] WindAPI not found immediately.");
                StartCoroutine(RegisterAsync());
            }
        }

        private System.Collections.IEnumerator RegisterAsync()
        {
            while (WindManager.Instance == null) yield return null;
            WindManager.Instance.RegisterProvider(this);
        }

        public void OnDestroy()
        {
            // DEREGISTER
            if (WindManager.Instance != null)
            {
                WindManager.Instance.DeregisterProvider(this);
            }
        }

        public void Update()
        {
            if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null) return;

            // UI Feedback Loop
            msgTimer += Time.deltaTime;
            if (msgTimer >= MSG_INTERVAL)
            {
                msgTimer = 0f;
                if (currentThermal != null && displayWindSpeed > 1f)
                {
                    string status = debugMode
                        ? $"Updraft: {displayWindSpeed:F1} m/s | Centering: {displayRadialFactor:P0}"
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

            // 3. Find Active Thermal
            currentThermal = FindThermalForVessel(v);

            if (currentThermal == null) displayWindSpeed = 0f;
        }

        // --- Calculation Logic ---

        private ThermalData FindThermalForVessel(Vessel v)
        {
            if (v.mainBody == null || !v.mainBody.atmosphere) return null;
            if (v.altitude > 30000) return null;

            Vector3d vesselPos = v.GetWorldPos3D();
            double vLat = v.latitude;
            double vLon = v.longitude;

            foreach (var t in thermals)
            {
                if (v.altitude > t.height) continue;
                if (Math.Abs(vLat - t.lat) > 4.0 || Math.Abs(vLon - t.lon) > 4.0) continue;

                Vector3d thermalPos = v.mainBody.GetWorldSurfacePosition(t.lat, t.lon, v.altitude);
                if ((vesselPos - thermalPos).sqrMagnitude < (t.radius * t.radius))
                {
                    return t;
                }
            }
            return null;
        }

        private Vector3 CalculateSoaringWind(CelestialBody body, Vector3 position, ThermalData thermal)
        {
            double partAlt = body.GetAltitude(position);
            Vector3d tCenter = body.GetWorldSurfacePosition(thermal.lat, thermal.lon, partAlt);

            float dist = Vector3.Distance(position, tCenter);
            if (dist >= thermal.radius) return Vector3.zero;

            float rFactor = 0.5f * (1.0f + Mathf.Cos((dist / thermal.radius) * Mathf.PI));
            float windSpeed = thermal.intensity * currentSolarFactor * currentAltFactor * rFactor;

            if (Mathf.Abs(windSpeed) > 0.01f)
            {
                displayWindSpeed = windSpeed;
                displayRadialFactor = rFactor;
            }

            Vector3 upVector = (position - body.position).normalized;
            return upVector * windSpeed;
        }

        private void LoadConfiguration()
        {
            // Same config loading code as before
            thermals.Clear();
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("KERBALSOARING");
            if (nodes.Length == 0)
            {
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