# MongoDB Movies

## Database & Collection

| Setting | Value |
|---|---|
| Database | `movies` |
| Collection | `movies` |
| Primary unique index | `csfd_id` |

---

## Document Schema (`movies` collection)

Defined in `Models/dtos/MovieDto.cs` with `[BsonIgnoreExtraElements]` for schema flexibility.

| BSON field | C# type | Notes |
|---|---|---|
| `_id` | ObjectId | Auto-generated |
| `csfd_id` | int | **Unique index**, primary key |
| `tmdb_id` | int? | nullable |
| `imdb_id` | string? | nullable |
| `title` | string | Czech title from CSFD |
| `original_title` | string | |
| `tmdb_title` | string? | English title from TMDB |
| `year` | string | |
| `duration` | string | |
| `rating` | string | CSFD rating |
| `description_cs` | string | Czech description |
| `description_en` | string | English description |
| `origin` | string | Country of origin (text) |
| `origin_country_codes` | string[] | ISO codes (e.g. `["US", "GB"]`) |
| `genres` | string[] | |
| `directors` | string[] | |
| `cast` | string[] | |
| `poster_url` | string | TMDB poster |
| `csfd_poster_url` | string | CSFD poster |
| `backdrop_url` | string | TMDB backdrop |
| `trailer_url` | string? | |
| `homepage` | string? | |
| `imdb_rating` | double? | |
| `imdb_rating_count` | int? | |
| `vote_average` | double? | TMDB vote average |
| `vote_count` | int? | TMDB vote count |
| `popularity` | double? | TMDB popularity score |
| `original_language` | string? | ISO 639-1 code |
| `adult` | bool | |
| `is_student_film` | bool | |
| `credits` | CrewMemberDto[] | Embedded array (see below) |
| `localized_titles` | `{lang: title}` | Dictionary, keyed by language code |
| `localized_descriptions` | `{lang: desc}` | Dictionary, keyed by language code |
| `release_date` | DateTime? | |
| `stored_at` | DateTime | Last write timestamp |

---

## Embedded: `credits` array

Defined in `Models/dtos/CrewMemberDto.cs`.

| BSON field | C# type | Notes |
|---|---|---|
| `tmdb_id` | int | |
| `name` | string | |
| `role` | string | e.g. `"Director"`, `"Actor"` |
| `photo_url` | string? | |

---

## Data Sources & Merge Strategy

Defined in `Models/MovieMergeExtensions.cs`. Movies combine data from two sources:

- **CSFD** — primary source for titles, ratings, cast/directors, Czech descriptions
- **TMDB** — supplements with posters, backdrops, trailers, English descriptions, ratings, localized metadata

---

## Key Queries

Defined in `Services/DatabaseService.cs`.

| Method | Filter |
|---|---|
| `GetMovie(csfdId)` | `csfd_id == csfdId` |
| `GetAllMoviesAsync()` | (all) |
| `GetMoviesWithMissingMetadataAsync()` | `tmdb_id == null OR description_en == null OR description_cs == null` |
| `GetMoviesWithMissingImdbAsync()` | `imdb_id == null OR imdb_id == ""` |

All writes use **upsert** on `csfd_id` — no separate insert vs. update paths.

---

## Related Collections

| Collection | Unique index | Purpose |
|---|---|---|
| `schedule` | `(date, movie_id)` compound | Showtimes per movie per day |
| `venues` | `venue_id` | Cinema locations |
| `premieres` | `csfd_id` | Premiere dates |
