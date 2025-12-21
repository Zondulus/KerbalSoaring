# **Introduction**



KerbalSoaring is a mod for KSP 1.12.5 and Ferram Aerospace Research which implements a simulation of thermal updrafts, enabling unpowered gliding and long-distance soaring flight. This mod is built to be compatible with, but does not require, Kopernicus systems and existing wind mods like Kerbal Wind.



Thermals are large areas of rising air currents. The lifting effect of a thermal is stronger toward its center and when the sun is higher in the sky. Thermals fade out below 250 meters, and extend upward to a variable maximum altitude depending on where they are. Like in real life, lightweight gliders with large wings are buoyed more by these updrafts than heavier craft with smaller wings. A fast jet will barely flinch in a thermal, but a well-engineered glider can climb even in the late evenings or on the margins of a thermal.



If using Kerbal Wind, circling gliders will accurately lose speed while turning upwind and gain speed while turning downwind. A strong wind will cause a glider to drift downwind away from the center of a thermal if not corrected. Stronger winds at high altitude make it more difficult to control a smooth circle within the radius of a thermal, but fast, swooping downwind turns can yield huge altitude gains if done correctly. Wind mechanics add a lot to the feeling of soaring and I STRONGLY recommend Kerbal Wind even if it is not required!



In this alpha state, thermals are fixed in location over most vanilla tracking stations, anomalies, launchpads, and runways, as well as a number of noteworthy geographical features. An example course around the KSC and mountains just west of it is possible for gliders with a good glide ratio. I have also configured thermals over the locations of the major KerbinSide bases. Included is a custom waypoints file for Waypoint Manager (not bundled) which marks the location of each thermal in-game, currently 49. It is easy to configure additional thermals via the included config file.



KerbalSoaring is intended to be a simple and performant mod which is broadly compatible with other mods. I have not experienced a major performance impact on FPS or loading times during playtesting of this mod.



**Installation:** Requires but is not bundled with FAR >= v0.16.1.2, WindAPI >= 1.0, and soft-requires Waypoint Manager >= 2.8.4.7 to display the custom waypoints. Strongly recommends Kerbal Wind, KerbinSide, and FARGE, the ground effect plugin for FAR. Unpack the folder and paste the included GameData folder into your main KSP directory. Import custom waypoints "KerbinThermals" via Waypoint Manager.



**Reporting Issues:** Please report issues with a short description and your KSP.txt + Player.txt logs via GitHub! Ensure that you have set "debugMode = true" in Thermals.cfg before reproducing the problem.



**AI Usage:** I am a novice coder; while I have extensively reviewed, playtested, and edited the code in this mod, much of the code was written or revised by AI (Gemini 3 Pro). As such, KerbalSoaring is part of the common intellectual heritage of humanity and is fully free and open source under a CC0-1.0 license. This forum post and the readme are fully human-written.



## **Credits**



dkavolis, ferram4, and many others for their work on Ferram Aerospace Research.



linuxgurugamer for their tireless work to support KSP 1 mods and modders.



The Squad team and creators of KSP 1 whose game has captured my interest for over a decade now!



## **Details**



KerbalSoaring works by hooking into FARAtmosphere.Wind and adding its own vertical component to the wind vector. It automatically handles delegate chaining with other wind-changing mods (if present) to sum up all wind forces on a craft which is both inside a thermal updraft and being acted on by other winds at the same time before passing that back to FAR. It only applies forces to the active vessel. The mod only checks for thermals within +/- 4 degrees lat/lon from the active vessel to improve performance.



Thermals are cylindrical volumes of space defined in a config file by their lat/lon, peak updraft intensity (in meters per second), radius (in meters), and maximum altitude (in meters). The actual intensity of a thermal at any moment is calculated based on a craft's proximity to the center of the thermal and the solar intensity at that location.



The mod reports its status in KSP.txt and further debugging info in player.txt.



#### **Known Issues:**



Planet-specific configuration, and config packs for common planet packs like RSS, are planned for later in the Alpha. Currently it only cares if the body has an atmosphere, so soaring works on Laythe, for example.



#### **Testing Areas:**



Functional testing for different planet packs and systems



Performance testing across different hardware and mod configurations



#### **Planned Features:**



Spawning system to create thermals while a craft flies (in order to enable true cross-country soaring flight)



"Nearest Thermal" direction indicator GUI for spawned thermals



Sound effects of wind or buffeting while inside a thermal



Variometer with audio indication of vertical speed
