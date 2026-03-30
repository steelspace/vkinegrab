# VenueScraper Test Fixtures

Minimal representative HTML/JSON snapshots used by unit tests.
Each file covers exactly the parsing logic of its scraper — not a full page dump.

## Refreshing a fixture

Fetch the live page and replace the relevant section:

```bash
# Plain HTTP (most art-house sites)
curl -s -A "Mozilla/5.0" https://www.kinoaero.cz/en > KinoAero.html

# JSON API (Cinema City)
curl -s "https://www.cinemacity.cz/cz/data-api-service/v1/quickbook/10100/film-events/in-cinema/1033/at-date/$(date +%Y-%m-%d)" > CinemaCityApi.json
```

## Platform groups & their fixtures

| Platform | Fixture(s) | Sites |
|---|---|---|
| JSON-LD Event | `KinoAero.html` (canonical) | Bio Oko, Aero, Světozor, Lucerna, Přítomnost |
| JSON-LD Event (Edison variant) | `EdisonFilmhub.html` | Edison Filmhub (`startDate` is array) |
| Cinema City API | `CinemaCityApi.json` | All 6 Cinema City locations |
| Server-rendered | `PremiereCinemas.html` | Premiere Cinemas |
| Server-rendered | `KinoAtlas.html` | Kino Atlas |
| Server-rendered | `KinoPonrepo.html` | Kino Ponrepo |
| Server-rendered | `KinoKavalirka.html` | Kino Kavalírka |
| Server-rendered | `KinoPilotu.html` | Kino Pilotů |
| Server-rendered | `KinoMat.html` | Kino MAT (dot-time format) |
| CinemAware CMS | `KinoRadotin.html` | Kino Radotín |

## Not yet implemented (no fixture needed yet)

- `CineStar.html` — JS SPA, Playwright rendering required
- `Dlabacov.html` — WP REST API / GoOut, endpoint TBD
- `Kino35.html` — JS-rendered, Playwright parsing TBD
- `KinoBalt.html` — JS WordPress, rendering TBD
- `Evald.html` — HTTP 429, bot-protection bypass needed
- `ModranskyBiograf.html` — HTTP 429, bot-protection bypass needed
