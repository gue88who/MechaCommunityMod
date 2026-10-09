# Mechabellum stats, version 1

Extension: `.mechstats`. Encoding: UTF-8 JSON. This is a statistics document,
not a playable `.grbr` replay. No binary dependencies or native pointers are saved.

| Root field | Meaning |
| --- | --- |
| `format`, `version` | `mechabellum-stats`, `1`; reject unsupported versions |
| `modVersion` | Recorder version |
| `id` | Unique recording-session UUID, unchanged through checkpoints/retries |
| `startedUtc`, `savedUtc` | When observation began and this snapshot was captured |
| `leftName`, `rightName` | Displayed player names, fixed to the report's sides |
| `ended` | Recording reached game over or left the match |
| `finished` | Game reported match completion; false for early departure |
| `outcome` | Displayed result if observed, otherwise null |
| `rounds` | Chronological round records |
| `overall` | Aggregated report, round number zero |

Rounds contain `number`, `complete`, `left`, and `right`. `complete` describes the
round statistics snapshot, not whether all events were observed. Each side has
`units` and `costs`. Overall damage, uses and hacking values sum rounds once;
overall costs use the latest cost snapshot, not the sum of each round's investment.

Version 0.9.66 adds optional round `info`: `winner` (`blue`, `red`, `draw`, or
`unknown`), `leftHpDamage` and `rightHpDamage` (HP lost by that player), and
`leftHpBefore`/`leftHpAfter`, `rightHpBefore`/`rightHpAfter`. Blue is the report's
left side; red is its right side, irrespective of the native team index. These
fields remain null when HP changes were not observed or the game uses a score
mode instead of player HP. Round winners use the game's result history.
Damage sums observed HP reductions; repairs do not subtract already dealt
damage. Ending HP can be negative. Before/after snapshots are retained separately.

Units and squads also have optional `coreDamage`: verified contribution to
**opposing** player HP loss. Native survivor scores must sum exactly to that
loss before attribution is accepted. `leftUnattributedCoreDamage` and
`rightUnattributedCoreDamage` retain HP damage dealt by each side that could not
be assigned to units. Null means unavailable; zero means a recorded zero.
Core damage sums across recorded rounds; incomplete attribution remains null.
The UI includes child contributions in parent totals once, as with combat damage.
Older version-1 files remain readable without these optional fields.

Version 0.9.80 adds optional squad `levels`: per-round snapshots with `round`,
`level`, `experience`, `nextLevelExperience`, and `maxLevel`. These are native
level and stored XP values, including XP carried from earlier rounds. They are
independent of the round's `xpEarned`. A round contains its latest observation;
`overall` retains the snapshot history and uses the latest observation for the
badge. XP progress is clamped to `experience / nextLevelExperience`; a full ring
does not change the recorded level until the game upgrades the unit. Maximum
level or an unavailable threshold has no next-level fill. Missing `levels` in
older files means unobserved. Spawned and converted squads retain their own
levels; parent combat totals do not combine levels.

Units contain `{kind,id}` keys, saved display `name`, `damage`, `overkill`, `taken`,
`takenOverkill`, `kills`, `casts`, `hacking`, `dealtSources`, `takenSources`, and
`contributions`. Both **damage and taken exclude overkill** in this file. Adding
the matching overkill field gives the bar's combined value. Spells exclude
overkill entirely, as in the report. Counts and amounts are signed 64-bit integers.

Source entries contain `source: {category,id,name}`, effective `damage`, and
`overkill`. Categories are `attack`, `tech`, `spell`, `other`. Incoming entries
describe the source of damage taken, not necessarily a technology owned by the
unit. Unobserved/unknown attribution remains explicit, never guessed.

Hacking fields are `successful`, `failed`, `pending` (progress amounts), and
`converted` (unit count). Pending is retained as data for incomplete rounds but
remains hidden in the UI. Subsequent death does not undo a successful conversion.

Version 0.9.71 adds optional squad `hacking` with the same fields. Stable squad
identity and army are captured with each hacking pulse, including the pulse that
completes a conversion. `successful` is the observed power from successful attempts;
`failed` is progress from attempts that ended without a conversion. `pending` is
active progress, settled as failed at round end if no conversion occurred. Each
cooperating Hacker retains its own progress; `converted` counts the target once.
Squad hacking values are a breakdown of unit totals, not additional totals. Older
files have `hacking: null` or omit it, so unrecorded individual amounts stay unknown
instead of borrowing the type total. All rounds sum recorded values; any missing
squad hacking observation keeps its aggregate amounts unknown.

Contributions include `origin` (`deployed`, `spawned`, `hacked`), `unit`, `name`,
optional `parent`/`parentName`, `child`, `tech`, `damage`, `overkill`, `taken`,
`takenOverkill`, and `kills`. These are attribution views of existing damage;
**do not add them to unit or army totals**. `child: true` denotes the producing
unit's view of a child's contribution; false denotes the actor's own entry.

Costs are a separate list keyed by unit, including units with no recorded hits.
`base` includes gifted deployed squads' value; `upgrades` and `technology` retain
the report's level/shared-tech accounting. Add those three fields for unit cost.
Spawned/hacked additions do not inflate deployed investment.

Version 0.7.0 adds optional `xpEarned`, `enemyXpAwarded` and `squads` fields to
each unit. Older version-1 files remain readable; absent XP defaults to zero
and absent squad details mean that individual packs were not captured.
XP values are fractional numbers, not damage integers. `xpEarned` is actual
observed XP growth after game modifiers/caps. `enemyXpAwarded` sums actual
opposing awards in the scoped death-XP calculation, including shared awards.
Neither value reconstructs XP missed by replay seeking.

Each squad stores a match-local string `id`, unique display `name`, `damage`,
`overkill`, `taken`, `takenOverkill`, `kills`, `xpEarned`, `enemyXpAwarded`,
`dealtSources` and `takenSources`. Like unit fields, saved `damage` and `taken`
exclude overkill. Deployed IDs use team and deployment index; temporary IDs
use a match-local sequence. IDs never contain native pointers. Squad values
are a breakdown of type totals: **do not add squads to unit/army totals**.
The same deployed squad merges across rounds in `overall`; different squads
of the same type retain separate entries.

Version 0.9.1 adds optional squad `parentId`, `parentUnit` and `spawned` fields.
They retain observed spawning provenance across rounds without storing native
pointers. Spawned units use their type name instead of a nickname; table child
rows group by parent squad and spawned type. Older files default to no parent
and `spawned: false`. A parent link is an attribution reference, not extra damage.

The same recording updates the same timestamp/UUID filename. A new recording
gets a new UUID even when player names and dates are identical. Names are stored
only inside JSON, so punctuation and Unicode cannot create invalid paths.
