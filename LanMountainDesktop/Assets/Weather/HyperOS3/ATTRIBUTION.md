# HyperOS3 Weather Assets (Official Xiaomi Package)

These assets were extracted from the official Xiaomi Weather APK provided by the user:
- Source APK: `c:\Program Files\Netease\GameViewer\Download\MI SKY 12.apk`
- Package: `com.miui.weather2` (Mi Weather)
- Extraction date: 2026-03-03

Extracted source paths inside APK:
- `assets/map_custom/particle/sun_0.png` -> `hyper_sun_core.png`
- `assets/map_custom/particle/sun_1.png` -> `hyper_sun_ring.png`
- `assets/map_custom/particle/fog.png` -> `hyper_fog.png`
- `assets/map_custom/particle/haze.png` -> `hyper_haze.png`
- `assets/map_custom/particle/rain.png` -> `hyper_rain_drop.png`
- `assets/map_custom/particle/snow.png` -> `hyper_snow_flake.png`
- `assets/map_custom/skybox/top.png` -> `hyper_sky_top.png`
- `assets/map_custom/skybox/back.png` -> `hyper_sky_back.png`
- `assets/map_custom/skybox/front.png` -> `hyper_sky_front.png`
- `assets/map_custom/skybox/left.png` -> `hyper_sky_left.png`
- `assets/map_custom/skybox/right.png` -> `hyper_sky_right.png`
- `assets/map_custom/skybox/bottom.png` -> `hyper_sky_bottom.png`
- `assets/map_assets/VM3DRes/cross_sky_day.png` -> `hyper_cross_sky_day.png`
- `assets/map_assets/VM3DRes/cross_sky_night.png` -> `hyper_cross_sky_night.png`

Extracted weather icon paths inside APK (`res/*.webp`):
- `res/aO.webp` -> `Icons/icon_sunny_day.webp`
- `res/k2.webp` -> `Icons/icon_moon_clear.webp`
- `res/Ip.webp` -> `Icons/icon_partly_cloudy_day.webp`
- `res/HI.webp` -> `Icons/icon_partly_cloudy_night.webp`
- `res/E4.webp` -> `Icons/icon_cloudy.webp`
- `res/5f.webp` -> `Icons/icon_rain_light.webp`
- `res/fO.webp` -> `Icons/icon_rain_heavy.webp`
- `res/lV1.webp` -> `Icons/icon_thunder.webp`
- `res/mH1.webp` -> `Icons/icon_snow.webp`
- `res/jB.webp` -> `Icons/icon_sleet.webp`
- `res/Wl.webp` -> `Icons/icon_haze.webp`
- `res/Mg.webp` -> `Icons/icon_windy.webp`

Use only according to Xiaomi's applicable license and usage terms.

## Soft Widget Icon Set (2026-03-05)

To better match the Xiaomi weather time-card visual hierarchy, an additional local icon set was generated for this project:

- `Icons/icon_hero_sun_soft.png`
- `Icons/icon_hero_moon_soft.png`
- `Icons/icon_mini_partly_cloudy_day_soft.png`
- `Icons/icon_mini_partly_cloudy_night_soft.png`
- `Icons/icon_mini_cloudy_soft.png`
- `Icons/icon_mini_rain_light_soft.png`
- `Icons/icon_mini_rain_heavy_soft.png`
- `Icons/icon_mini_storm_soft.png`
- `Icons/icon_mini_snow_soft.png`
- `Icons/icon_mini_fog_soft.png`

These files are original derivative assets generated in-repo with local tooling, using the extracted Xiaomi package visual direction as reference (soft glow hero icon + lightweight forecast icons).
