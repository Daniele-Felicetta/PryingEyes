# The desert

Request of 18 November 2026: when a new game starts, the menu offers two ways to play, the standard one the game already has and **the
desert**. A huge, almost endless, procedural desert; the game becomes open world, with a sky that goes through day and night. You start in
the cathedral you already have, go down its steps and put your feet on the sand at the beginning of a day. Around you stand small and large
buildings made of the rooms of the floors, but pulled apart: some whole, some broken, some split in pieces, some falling apart. There are towers
with more floors than any branch of the standard game, seen from below as they really are, and entrances that lead down to catacombs. The
bosses wait on the last floor of the towers and the catacombs. The perk books and the weapon consecration are in the cathedral, to unlock with
enough cathedral resources. Out there only poor, common weapons are found; every other weapon is in the cathedral, for twice the points. In the
rooms, the buildings and out in the open there are only weapons, points, ammunition, medikits and essence.

After the first playtest (the same day) the player asked for more buildings and more variety, outsides that are not buggy, no stealth mechanics (nobody
had asked for them) and a livelier world, with serious optimisation of the graphics if needed at the same quality. Hence the six regions, the finished
outsides and far silhouettes of the places, the things on the sand, the weather, the wildlife and the camps below.

This page says how it plays, what is found where, how the world is made, the code, the decisions that were left to the developer, and what is
verified. The cathedral, the works, the resources and the satchel are those of [cathedral-and-catacombs.md](cathedral-and-catacombs.md); the
rooms are those of the floors ([tower.md](tower.md)).

## How it plays

1. *Gioca* (and *Ricomincia dalla cattedrale*) opens **Come vuoi giocare?**: **Standard** or **Il deserto**. The choice is remembered for the
   next time (`PlayerPrefs` key `PryingEyes.GameMode`; the automated tests never write it). The command line `-deserto` forces the desert.
   Co-op and split screen stay in the standard mode (the desert is for one player).
2. You wake on the churchyard (*il sagrato*) of the cathedral, at **07:00**. The cathedral stands on a plinth of the same stone as the church facades
   (a darker base under a cornice, a lighter course at the foot); from a 14 m gap in the churchyard's parapet a wide stair of twelve steps goes down to the sand
   (a stone riser and a lighter slab with a nosing for each step, a sloping wall with a coping on each side and a pier at the foot), and a brazier burns on
   each side at the top (a wide shallow bowl of cast iron on a tripod held by a ring, with the coals inside): flames, a hot core, smoke and embers. Two banners of crimson cloth with the
   golden eye of the game hang from iron poles beside the stair and the wind moves them; the lamp posts (turned iron, a six-sided lantern with its glass) and the stalls of the market (a striped
   awning with a scalloped valance) are made for the desert too. Nothing attacks in the cathedral: the walkers have no navmesh there (they stay at the foot of
   the stair) and whatever flies over the cathedral disappears.
3. **A day lasts twenty minutes** (about twelve of daylight, eight of night; `DesertClock`). The sun, the moon, the stars, the fog and the
   exposure follow the clock; indoors (anything within 12 m above the head) the look goes back to that of the rooms. The top of the screen
   counts the days, and the objective is on two lines: first the hour, the phase (dawn, day, dusk, night), the nearest place and, when you are
   more than 60 m from the cathedral, the way home, each with the side it lies on as seen from where you look (*davanti*, *a destra*, *dietro a
   sinistra*...: sectors of 45 degrees, `DesertWorld.Sector`); then, dimmer, what to look for.
4. **The places** stand on the sand, in cells of 140 m (one place per cell at most) at least 170 m from the cathedral: houses, chapels, inns,
   churches, hotels, theatres, sanatoria and complexes, made of the same rooms as the floors, but **whole, ruined, split in pieces or
   collapsing** (the shares depend on the region: 42% whole in the hamlets, 16% in the dead town); **towers** of 7 to 14 floors (up to 76 m) and
   **mausoleums** whose stair goes down through a well in the sand to **catacombs** of 3 to 5 levels. How many cells hold a place, and which
   kinds, depends on the **region** you are in (below): from 92% of the cells in the hamlets to 28% on the salt flats, about twice as many places
   per area as the first version (59% of the cells on average, and the cells are smaller). Each place is built when you come within 280 m (a tower, 340 m) and furnished when you are close, a room
   at a time; farther away, out to 760 m, you see its **far skin**: the same outside, made of a few hundred vertices, with the roofs and the lit
   windows at night, which goes out when the real shell is complete.
   **The outside of every place is finished**: roofs (pitched, hipped or flat with battlements), a plinth, cornices, pilasters and buttresses,
   window frames with shutters, blind windows where there is no room, rubble at the foot of a ruin and jagged tops on the broken walls; the wall
   colours are a palette of the place's kind, chosen per place.
   **The regions.** The desert is divided into six kinds of land of about a kilometre across, each with its own sand colour, dunes, density,
   places and things: the **dunes** (the sand of the shader, the highest dunes), the **hamlets** (dry earth, many small houses, inns, wells and
   windmills; the cathedral's region is always this one, so the first look from the steps already holds houses), the **holy land** (pale chalk, chapels,
   churches, catacombs, columns, steles and obelisks), the **dead town** (ash grey, hotels, theatres and sanatoria falling apart, wagons and bones),
   the **badlands** (red, many mesas, towers and catacombs, red rocks and layered slabs) and the **salt flats** (white, flat and almost empty, bones
   and white rocks). The borders blend over 160 m. On top of the land stand the **buttes**: lone rocks of layered stone, 28 to 95 m tall, with a flat top, walls
   that are nearly vertical and a skirt of rubble round the foot, one at most in a square of 900 m (four squares in five in the badlands, one in four to one in
   five elsewhere, hardly any in the hamlets), never within 250 m of the cathedral and never under a place: a mark on the horizon to walk to.
5. **Towers and catacombs are stacks of levels** joined by a stair room with two flights; the **boss** is in the hall of the last level (the top
   of a tower, the bottom of the catacombs). Which branch's boss it is, and so which trophy it leaves, depends on the place. When it falls its
   **trophy goes straight into the cathedral's store** (three of its resource and the usual common and rare ones) and the place never puts it
   back in this game.
6. **What you find.** In rooms and in the open: **points** (gold coins), **ammunition** (a crate: half a magazine reserve), **medikits** (50
   health) and **essence** (a crystal: 30 of the left hand's essence). Walk within 1.5 m to take them; what you took stays taken. **Weapons**
   are free panels on a wall (*DA PRENDERE*) or on a wooden stand in the open, and only the poorest (grade I; grade II in a boss's hall).
   Ammunition for a weapon you own costs as always (150 points).
7. **The cathedral keeps the rest.** Its new **armoury** (the room east of the side aisle) sells every weapon except the starting pistol, one
   panel each, **at twice the price** of the standard game. The **Library of Vows** (perk books) and the **Altar of Consecration** are works
   that exist only in the desert: build them in the cathedral with resources (they show as construction sites like any other work). The
   Library offers three perk books on three lecterns in the nave (2000 points the first, then 2500, 3000, 4000; four at most); take one and the
   other two close, and 45 seconds later, with you away from the lecterns, it offers three new ones (`DesertDirector.LibraryRenewDelay`; the
   other floors have a library each, the desert has one, so it renews). The Altar consecrates the weapon in your hand for 3000 points, once per
   weapon. The store, the market and the other works are the usual ones, and everything you pick up goes **directly to the store** (as in
   direct play): there is no satchel to bring home.
8. **Enemies.** Monks mostly, and the farther from the cathedral the tougher and the more varied (abbots, eyes, flagellants, plague bearers,
   statues; theatres and sanatoria have their own): their health and speed grow with the distance (`DesertMode.Danger`: 1 at the cathedral,
   4 at 4.5 km). **There is no stealth in the desert** (the floors' cones of sight, hearing, footstep noise and the takedown are off): whoever is born
   there **rests** (`Enemy.dormant`), wandering in their zone without noticing anything, until you come within 30 m (48 at night), or someone hits
   them, or one beside them (16 m) wakes; then they know where you are and come for you, like the enemies of the rounds. If you run farther than
   130 m (220 for a boss) for seven seconds they give up and go back to rest where they were (`DesertDirector.WakeTick`). In the open there are
   few by day and many at night (target 1 + 0.8 danger by day, 4 + 2 danger at night, at most 12).
9. **Dying** costs what it costs in the floors (half the points, the last perk) and you wake at the top of the cathedral stair. A safety net
   brings back whoever falls under the sand. Nothing of the desert is saved: the world comes from a seed, a new game makes another one, and
   *Salva e torna al menu* says so.
10. **Sound.** On the sand the steps are those of sand (`SoundGen.Step` surface 3: a soft rub and a few grains; to the monks' ears 20% quieter
    than marble, a little louder than carpet); under the open sky there is wind in slow gusts, no hum of a room, no organ, no creaking, and
    almost no echo (0.45 s); in the nave the echo is the church's (3.6 s). Inside a place (a roof within 12 m above the head) the hum of a room and
    a room's echo come back (`ProcAmbience`). The wind follows the weather (`DesertWeather`): a breeze in slow gusts, louder and higher in pitch
    in a storm, when the **hiss of the sand** joins it. At night the **crickets** sing from dusk (fewer in a strong wind, none in a storm), now and
    then a **howl** comes from far off, by day a **vulture** caws from the flock that circles over the nearest ruin, and near a burning camp fire there
    is its **crackle** (3D, heard within 34 m). All of it stops indoors, follows the *Ambiente* volume and is made by code like every other
    sound (`SoundDeserto.cs`, `DesertSound`).
11. **The air, and what lives in it.** The wind blows all day (it bends the bushes, rolls the tumbleweeds, turns the windmills and is heard) and about
    one day in two has a **sandstorm** between 08:00 and 22:00 (never before noon of the first day) that lasts 75 to 160 seconds of play: the fog gets up to
    3.8 times thicker by day, the sun loses half its strength, the sky and the fog turn the colour of sand, streaks and clouds of sand fly past
    (`DesertWeather`, `DesertDust`) and the animals take shelter. By day **lizards, scorpions and beetles** live on the sand and run away when you
    come near (6.5 m, 3.8 for the scorpions), **tumbleweeds** roll downwind, **vultures** circle over the ruins and the towers (up to five flocks within
    700 m, always over the same ruins) and at night **bats** take their place (`DesertLife`). Here and there are **camps**: a fire ringed with stones, one to
    three tents with their doors to it and a pile of supplies; by day the fire is out, at night it burns with a flickering light, smoke and sparks
    (`DesertFires`). And **landmarks** that can be seen from afar: wells, obelisks (8 to 14 m), ruined windmills whose sails turn with the wind, broken
    wagons. Everything on the sand is a pure function of the seed (`DesertScatterGen`), so it is the same when you come back.

## What is found where

| | Cathedral | Places and open sand |
| --- | --- | --- |
| Weapons | all, in the armoury, at **twice** the price | only grade I (II in a boss hall), free, a few |
| Perk books, consecration | yes (works to build with resources) | no |
| Points, ammunition, medikits, essence | no | yes |
| Resources, trophies | the store, the market, the works | the bosses' trophies go to the store; enemies' drops go to the store |
| Enemies | none | patrols, guards, bosses |

## The world

A pure function of one seed (`Scripts/Deserto/DesertWorld.cs`, checked without Unity by `tools/desertlab`): nothing is stored, a place is
rebuilt identical every time you come back.

- **The ground.** `DesertWorld.Height(x, z)` (`Raw` is the same without the pads of the places): broad swells of noise, taller with the distance (5.5 m near,
  15 m far) and with the region (a quarter on the salt flats, one and a half times in the badlands); on top of them **transverse dunes** (`Dunes`: crests
  that run across the wind and wander, a long gentle windward face and a slip face of about 30 degrees, 120 to 230 m from crest to crest and up to
  1.6 times farther away, in fields with flat floors between them; 9 m high near home and 18 m far, in the dunes region; the salt flats have almost none) and **mesas**
  (flat tops 7 to 22 m high with walls a few metres wide, a second step on the big ones and eroded faces; more of them, and half as tall again, in the badlands)
  and the **buttes** (`Buttes`, `ButteOfCell`): every square of 900 m may hold one, chosen by the hash of the square and the chance of the region at its middle (80% in
  the badlands, 30% on the salt flats, 26% in the dunes, 22% in the holy land, 18% in the dead town, 6% in the hamlets), put in the middle 40% of the square so
  that nothing of it crosses into the next one. A butte is an ellipse 60 to 130 m long (0.55 to 1 as wide), 28 to 95 m tall: a rubble skirt (a fifth of the height,
  a slope that can be walked) out to 1.55 radii, a wall of up to 84 degrees between 0.8 and 1 radii, cut by gullies and bulging because the outline is moved by two
  scales of noise, and a flat top that rolls a little. The skirt stays 250 m from the cathedral and 120 m from the welcome chapel and the landmark tower, and
  no place is made within 45 m of one (`ButteTouches`, in `MakeSite`: a pad cut into a wall would be a cliff with a floor). The sand shader paints the walls in strata like the
  mesas. The first one is usually a long walk away: with the seed 1000 the nearest skirt is 600 m from the cathedral.
  Dead flat for 55 m around the cathedral (the sand level is -2.4 m, the church floor 0) and growing to full relief over the next 145 m, calmed to 62%
  of a place's level within 40 m of it and flattened to a pad under every place. The world ends at 18 km (`DesertWorld.WorldRadius`; floats lose
  precision beyond it): from 17.5 km a line says the desert ends ahead, and whoever goes past 18 km is put back 100 m inside, with a banner
  (`DesertDirector.EdgeTick`). To *see* the ground without Unity: `dotnet run -c Release -- --map out.ppm x z size px [seed] [h]` in `tools/desertlab`
  writes a hill-shaded, elevation-tinted map of any square.
- **The regions** (`DesertWorld.RegionAt`, `RegionBlend`). The Voronoi cells of a random point in every square of a kilometre; the weights of the six
  kinds are 26% dunes, 17% hamlets, 15% holy land, 15% dead town, 17% badlands and 10% salt flats, and the cathedral's cell is always the hamlets.
  Each region has a share of cells with a place, a table of kinds of place and one of states, a dune height, a sand colour (a multiplier and a loss
  of colour, written in the vertex colours of the sand: `DesertSand.shader` multiplies its own colours by it) and a table of things on the sand.
  Two regions meet across a border blended over 160 m.
- **Fog and sky.** Exponential fog, 0.0016 by day (31% of the sand is still visible at the edge of the terrain, 740 m, and 9% at 1,500 m: the horizon
  blends into the sky and the far places show in colour) and 0.009 at night; the camera's far plane is 1,500 m. The dome (`DesertSky.shader`) has a gradient
  that changes fast near the horizon and slowly towards the zenith (the horizon and the fog are brightened by day, the zenith and the clouds by less: with the same gain
  everywhere the tone mapping turned the blue into milk), two blue-grey ridges of far mountains, a sun with a wide and a narrow glow and a disc, **low clouds** and high streaky cirrus.
  The low clouds are a height field of two octaves of 3-D noise (big masses with finer detail, `CloudField`) cut at a threshold that gives the cover (6% of the sky at 0.1, 38% at 0.3) and
  eroded at the edges so that they fray; they are shaded by their thickness (a thin bright edge, a grey dense core) and by the slope of the field towards the sun (it is sampled two steps
  that way: where the density falls the edge is lit, and it carries a silver lining). With the sun low and in front, **rays** fan out of it: streaks from noise on the direction of the
  offset from the sun, within 60 degrees of it, at most a fifth of its light and only on the sky (the sand's mirror of the sky, `MirrorSky`, has the clouds but not the rays or the cirri).
  At night it has stars in cells of a third of a degree, the moon
  and a **Milky Way**: a wide pale band along a tilted great circle, denser on one side, with dark dust lanes and a veil of tiny stars, dimmer when the moon is up and covered by the clouds. The disc and its core are added after the
  clouds and the ridges, so that a thin cloud dims the sun instead of hiding it and the far mountains cover it when it is low. The sun's halo is **mixed into** the sky
  (a `lerp` towards the sun's colour, in the dome and in the fog's own copy of it), never added: an additive glow on a pale sky turned it milky white. **The light of the
  day** is scaled by the height of the sun (`DesertSky.DayLight`, 0.58 at noon, on the sun and on the three ambient colours, not on the exposure: with the exposure the
  unlit sky went grey, with the light the sand has a range of tones and the sky keeps its blue), and the **eye adapts** when you look into a low sun (`SunAdaptEV`: up to 0.9 EV
  darker while you face it, falling in about 0.6 s and recovering in about 2 s). The **fog thins with the height** (`HeightFog` in the sand and prop shaders: the mean of
  e^(-0.015 y) along the ray, an exact integral because URP's exponent grows with the length of the ray): the top of a butte or a tower comes out of the haze, darker and
  nearer, and the foot stays veiled.
- **Grading** (`DesertGrade.cs`, `DesertSky.ApplyGrade`). The floors have a colour look of their own (the church look, for instance, takes 26 points
  of saturation out and splits the tones towards cold): left on, it made the open desert look like a grey church. `DesertSky` keeps the look of the room
  it replaced and, outdoors, overwrites the volume's saturation, contrast, colour filter, shadows and highlights with `DesertGrade.Outdoors(sky, weather)`: an
  interpolation between four looks (day: a little more colour and contrast, a warm filter and almost neutral highlights, since URP's split toning is a soft-light blend and a
  strong warm tint on the highlights lifted the red of the whole sky and turned it grey-lilac; golden hour: warmer, a stronger bloom and a vignette; night:
  cool, a touch desaturated, grainy; storm: dusty and flat), all pure and checked by the lab (`GRADE_*`). Each look also sets the **bloom threshold** (2.6 by day,
  1.7 at golden hour, 1.0 at night, 1.8 in a storm; `DesertSky` writes it into the volume's Bloom): the bloom works on the picture before the exposure, and in the sun the
  sand is above 1 almost everywhere, so with the rooms' threshold of 1 the whole open desert glowed like milk. Inside a building it hands the look back
  (`CaptureIndoor` / `Resync`, called from `FloorDirector.ApplyLook`, because the sky is made before the look is applied). The shadow distance is three
  times the floors' value outdoors (`GameSettings.SetShadowRange`: the long shadows of low sun and of the dunes) and back to 1 inside.
- **Below the sand.** The levels of a catacomb are 5.4 m apart, down to 21.6 m under its entrance, so the world height of a room is negative.
  Anything of the rooms that was tuned for a floor at height 0 must use the height above the room's floor, never the world height: the haze
  of the church naves (`ChurchHaze.shader`) did not, and in the catacombs it was e^(19 x 0.48) = 9,000 times denser, so the boss hall was a solid
  grey fog (found with `-desertdiag`, fixed by measuring from the volume's floor; in the floors the floor is at 0 and nothing changed).
- **The places.** The plane is split in cells of 140 m; each cell has a place with the probability of its region, with a kind, a state, a footprint
  (kept 12.5 m inside the cell) and a seed, all from the cell and the world seed. The cell (0, -1) is always a ruined chapel (the welcome chapel, at
  100 m) and (0, -3) a tower of at least seven floors (the landmark, at 350 m).
- **The generator.** `PlanGen.Building` (`Scripts/Proc/PlanGen.Building.cs`) joins rooms of the floors in a building of a given state;
  `PlanGen.StackLevel` makes one level of a tower or a catacomb (a stair room that is identical on every level, a hall, five other rooms).
  Both are checked on hundreds of plans (`BuildingFault`).
- **The sand** (`DesertTerrain`, `DesertTerrainGen`). Square blocks of 64 m around the player with three levels of detail (a vertex every 2 m
  up to 150 m, every 4 m up to 380 m, every 8 m up to 740 m, then fog), computed on worker threads and turned into meshes two per frame.
  Only the near blocks have colliders. A grid line goes through every edge of a well, so the hole is cut exactly; skirts hang under the edges
  so two neighbours of different detail never show a crack (but not across a well that straddles two blocks: a skirt there would be a wall of
  sand in the middle of the stair, and was, until a player of the test stood on the landing of a catacomb and could not cross it). `DesertSand.shader` paints it with no texture: patches of light, dark and red sand, long streaks along the wind that break the monotony at middle distance,
  wind ripples at three scales that bend the normal (the third, about ten metres between crests, is seen out to 450 m, so that the far ground is marked like a view from a plane;
  low sun gives every crest a shadow: crests are crooked lines across the wind, bent by two
  scales of noise, with an asymmetric profile and a height that changes slowly, so there are smooth areas and deeply furrowed ones; no cell noise,
  which read as a grid at low sun; and the wind is not the same everywhere: a phase shift that changes over a hundred metres (`WarpN`) turns the crests by about twenty degrees
  and widens or narrows them, a displacement of the ground a few metres wide (`RippleBend`: a maximum every nine metres, up to three metres of shift) bends them from one spot to the
  next, and the gradient of both bends the normal, so the ripples are no longer stripes all parallel to the horizon, nor a ploughed field), grain close up, in patches of tens of metres a
  **pavement of pebbles** at two scales (within 34 m; each stone is rounder than it is long, has its own tint - dark brown, rust, grey or pale chert - lighter on top and darker at the rim, its own dome in the normal and a contact shadow: they were almost black and flat, and with the sun behind the eye they read as oil stains), a glint of quartz (a few pinpoints that catch the sun, within 13 m), and, where the ground is steeper than the sand can lie
  (about 37 degrees: mesa walls, the flanks of the tallest dunes), rock in horizontal strata of four colours with dark joints and vertical erosion
  streaks; on the salt flats the crust is split into plates a few metres across, with darker cracks, and about a third of the flats' interior is under a **film of water** (`DesertWorld.Brine`: lakes a couple
  of hundred metres across, none within the strip where the flats meet another region). The lake is not geometry: the terrain generator writes it into the top fifth of the sand's vertex alpha
  (`BrineAlpha`: 0.8 to 1 is water, as the alpha of the flats is 0.8 and no other region gets there), the shader reads it back with a ragged shore (a threshold on the vertex value plus noise, so the shore
  does not have the outline of the triangles), darkens the salt round the lake (damp) and, much more, under the water (a lake of white salt was a snowfield), flattens the normal to a ripple a few thousandths wide that the wind moves, and mirrors the sky with a
  Fresnel that never quite reaches 1 (from a few metres you see the bottom, at a low angle it is nearly all sky, in lanes that the wind streaks). The mirrored sky is the sky's own formula ported into the sand shader (`MirrorSky`: gradient, the halo and the disc of the sun, the moon, the stars and the
  low layer of clouds, with the same noise and numbers, so a reflected cloud is where the real one is), without the cirri and the Milky Way; the horizon is the fog's colour, so a far lake melts into the haze.
  Walking in it the step is a splash (`SoundGen.Splash`: the thud of the foot, a wash of noise and a few bubbles) and leaves no print and no dust. By day, with the air clear, the **shadows of the clouds** (`CloudShade` in
  `DesertSand.shader` and `DesertProp.shader`, strength `DesertSky.CloudShadow`, written to `_DSCloudShadow`) slide over the ground and the things on it: soft patches a couple
  of hundred metres across that take a third of the light (and more of its red than of its blue: the shadow loses the sun and keeps the sky, so it is cool and not mud),
  drifting with the wind at 5.5 m/s, thresholded by the sky's own cover; the sand and a rock use the same function, so
  they darken together.
- **The navmesh** (`DesertNav`). One `NavMeshData` filled in tiles of 64 m (the same step as the blocks) around the player, each tile built
  asynchronously and redone when a place appears or goes away in its area; the far tiles are emptied. The furniture roots are obstacles, the
  cathedral is one big obstacle (the sanctuary). `HotelNav.Ready` follows the first tile. A tile is only built when the sand under it (and
  around it, for the border) has its collider, and the update says `preserveTilesOutsideBounds`: without it Unity throws away every tile
  outside the bounds of the update, so each new tile erased all the others.
- **Streaming of a place** (`SiteRt`, `DesertDirector`). Every place is a root built at height zero (the furniture of the floors is placed in
  absolute coordinates) and then moved to the height of the site; to furnish a room later the root goes back to zero for a moment. First the
  shells of all the levels (walls, floors, ceilings, stairs), then, only near the player, the furniture, the loot and the guards, a piece at a
  time within 7 ms of a frame; far away the furniture is taken off again and, farther still, the place disappears.
- **Outsides and far skins** (`FloorBuilder.Facade.cs`, `FacadeKit.cs`, `SiteFacade.cs`). The rooms of the floors are made for whoever stands inside
  (ceilings look down, walls look in), so an unfinished building looked like an open box from the sand. `FloorBuilder.FacadeBuild` builds the outside
  of a plan: roofs over every room (tiles, slate, lead or tar, by theme; flat roofs with a parapet and, on a tower, battlements), step walls where two
  rooms of different heights meet, a plinth, a cornice, pilasters and buttresses, window frames with shutters, blind windows, rubble at the foot of
  the ruins and the jagged tops of broken walls (`Room.crumble`); a doorway that a lowered wall cuts below the height of a person (`Opening.Gap`: a floor-level opening under 2 m,
  which 4 buildings in 10 have) keeps no lintel, no soffit and no frame bar, so the wall simply breaks there and the navmesh agent (1.9 m) walks through (before, the door of a collapsed
  building was a 1.5 m slot closed by a slab of ceiling, and nobody could enter three of the places the test visits); the wall material is tinted by the place's palette (0 to 63, `FacadeKit`). The same
  builder makes the **far skin** of a place (`SiteFacade`): no small details, about 1,100 vertices per place, built within 760 m of its edge and
  thrown away farther; it stands 4 cm out from the real wall so that the two never fight, and it goes out when the real shell is complete
  (`SiteRt.ShellComplete`). A far skin has lit windows at night (`FacadeKit.SetNight`). At the foot of the outer walls of the near shell the **sand has piled up** in ramps (`FloorBuilder.Drift`: four rows of
  drifts 8 to 58 cm high and 0.5 to 2.4 m wide, shaped by noise in world coordinates so that neighbouring walls agree, none in front of a door, on a tower or a catacomb only at its
  entrance level), drawn with the sand's own shader and the region's colour in the vertex colours (`FacadeKit.Sand`, `MeshBuilder.QuadSmoothC`): the walls no longer stand on the
  ground like a model on a table. The stone of the churches is lightened about twice (`FacadeKit.StoneGain`: the kit's stone has an albedo of 0.12, made for dark corridors, and next to the
  sand a church was a black block); the inside keeps the kit's material as it is.
- **The banners of an intact tower** (`DesertBanner.OnTower` and `OnTowerWalls`, made by `SiteFacade` with the last piece of the far skin and kept when the real shell takes its place;
  a ruined tower has none). A mast with a banner twice the size of those of the stair stands in the middle of the largest flat roof of the top floor (the beamed ceiling inside is
  a flat slab from outside), and under the cornice a **banner hangs down each long wall**: one per side, on the longest stretch of outer wall of the top floor if it is at least 7.5 m (up to four; 4.2 m
  wide, 11 m long, on an iron bar held 96 cm out from the wall by two arms). The first version had only the mast, and the photos showed why that was not enough: a roof 43 m up is not seen from
  the ground nearby, the line of sight over the parapet hides the cloth from anyone within about 150 m (from 90 m only the finial showed, and what looked like a tan block at the corner of the
  crown was the cloth, cut by the parapet). The wall banners are seen from every side and close up, the mast from far away. The wind moves them all (`DesertSky.Weather`: strength, gusts,
  direction), but a wall banner is never pushed into the wall: its displacement toward the wall is cut to a quarter and stops 45 cm out, so the cloth stays at least 35 cm from the facade (80 cm at rest; the cornice juts 22 cm).
  That bound holds by construction; the test measured the nearest it came in the light wind it met (46 cm at the tower's banners, when the limit is 25).
- **Things on the sand** (`DesertScatterGen`, `DesertScatter`, `PropMeshes*.cs`, `DesertProp.shader`). Every 64 m square (the sand's own grid) has its
  props, computed from the square's coordinates and its region only, on a worker thread: tufts, pebbles, rocks of four tints, boulders, standing slabs,
  cacti, bushes, dead trees, bones, columns, steles, cairns, tents, fires, crates, wells, obelisks, windmills and wagons; none on a place's footprint
  (7 m margin), on the cathedral's apron or on steep sand; some come in groups (a patch of grass, a colonnade, a camp). **The yards**: the sand by the walls of a building is not
  bare. Each house, inn, church, hotel, theatre or complex gets three to seven props 1.4 to 4 m outside its footprint (two to four for a house or a chapel): crates and barrels, a wagon now and then, bushes,
  a dead tree, tufts, bones, and by a church steles, cairns and a broken column; none within 4.2 m of a door that opens outside, none on another place, and in a ruin a third of the
  crates and barrels are bones instead (it was looted long ago). A yard is a pure function of its place's own seed (`DesertScatterGen.Yard`): every square that it touches asks for the same list
  and keeps the props that fall inside it, so the squares agree without talking to each other (the lab checks it: `YARDS_*`). They are drawn instanced, thousands
  per frame, with a single material (`Resources/Deserto/DesertProp.mat`): the colour is in the vertex colours, the bending in the wind in the alpha,
  a flame or a glow in UV0.x, the kind of material in UV0.y (`PropMeshes.Surf`: stone, organic or plain), and every copy has its own brightness from its
  position. The fragment adds the grain: noise in the object's own space (so it sticks to the thing even when it moves) with analytic derivatives that move
  the normal; stone gets large patches, fine grain, strata, a coat of sand on the faces that look up and at the foot, and a contact shadow; wood and plants
  get streaks along their height. Rocks are blobs cut by random planes (`RockPlanes`: a smoothed convex polyhedron, with a tint per face) instead of
  lumps (the edges are bevelled by about 5%, a second octave of noise chips the faces and the colour goes in strata that wander with the noise; a rock went from about
  480 to 1,670 triangles and a boulder from about 980 to 3,580), the slabs are rough boxes with displaced faces and chipped tops, the dead trees fork, a bush has sixteen stems with three tufts of
  sage-green leaves on each (the old single yellow ball on a stem read as a lollipop) and a cactus has eight flutes on the trunk and six on each arm, four sides to a flute, with the
  shape and the colour following the same cosine and the tips rounded into domes (it had two sides to a flute and, with the normals averaged, looked smooth, like plastic). Small things are drawn out to 150 m, middle ones to 280 m, big ones to
  520 m. Rocks, boulders, cacti and bushes, the heavy ones, come in two versions: all the triangles within 70 m of the player and, beyond, the same outline with fewer
  (`PropMeshes.GetLow`: the same planes for the rocks, about a fifth of the triangles; fewer rings and sides for a cactus, a single bigger tuft on each stem of a bush; each copy chooses by its own distance when the lists are made, every 20 m of walking). Nothing that lies on the sand is sent to the card
  when the player is more than 2.5 m under it, in a catacomb, where it cannot be seen (`DesertScatter.Underground`: the props alone cost 2.5 ms in the boss hall of a
  catacomb before, and 0.0 to 0.2 ms after). The solid ones have a capsule collider within 140 m of the player (pooled, named so that shots and steps sound right: stone, wood or cloth),
  which the navmesh sees. The two materials are assets in `Resources` because the build strips the instanced variant of a shader that no asset uses
  (`Graphics Settings > Instancing Variants: Strip Unused`), and a material made while playing would draw nothing.
- **Weather and life** (`DesertWeather`, `DesertSky`, `DesertDust`, `DesertLife`, `DesertFires`). The weather is a pure function of the seed and the clock
  (`DesertWeather.Evaluate`: wind, gusts, storms; the lab checks it). `DesertSky` applies it: the fog density is multiplied, the fog, the horizon and the
  zenith take the colour of sand, the sun is dimmed, the shaders get `_DSWind` and `_DSNight`. The fog also takes the sky's colour in the direction of the look, with the
  sun's glow on the horizon (the same terms as the dome: `DesertSky.Apply`), so that the far sand melts into the sky even against the sun, and so do the facades, which have
  URP's fog. `DesertDust` is two particle systems upwind of the player (streaks and clouds, Unlit: they are multiplied by `DesertSky.LightLevel`, or at the exposure of dusk and night their full colour was a
  white dash) and, under every step on the sand, two or three puffs of dust (`Footstep`, called by the player controller; bigger and faster when running). `DesertLife` animates by hand and draws instanced: 12 tumbleweeds, 20 animals that are born 12 to 58 m from the player out of sight and
  vanish beyond 75 m, and up to five flocks over the ruins; `DesertFires` gives the four nearest fires within 130 m a light, smoke and sparks; `DesertSound` plays what they sound like (the hiss of the sand, crickets, caws, howls, the crackle).
  **Sand off the crests** (`DesertSpindrift`): with a little wind, in the open and out of a storm, every second and a half the code looks for a dune crest in front of the player (the ground height, a pure
  function) between 45 and 230 m away: the highest point of a 60 m line along the wind through a random spot, if it lies in the middle of the line, is higher by 50 cm than the ground 30 m upwind and by 80 cm
  than the ground 30 m downwind (the lee face is the steep one) and is level across the wind within 90 cm at eight metres (a long crest and not a hump), and not within 20 m of one that already smokes. The first rule
  tried, a local maximum at six metres, found a crest in 3 % of the searches (none at all from a tenth of the vantage points: measured on 200 random points of the dune fields), because the real crests are narrow
  (a long gentle windward face and then the fall); this one finds one in 65 % of the searches (44 % from the worst tenth of the places) and the pace of the searches keeps it rare. A crest smokes for seven to thirteen
  seconds: thin veils that leave it, lift a little and run downwind at 2.5 to 13 m/s, stretched along their motion; seven crests at most, one particle system emitted by hand, Unlit (multiplied by `DesertSky.LightLevel`).
  **Footprints** (`DesertPrints`, `DesertPrint.shader`): each step on the sand leaves the print of a boot, feet alternating and a little turned out, which the wind erases in 80 s (at most 96
  at once, the oldest gives way). A print is a flat quad laid on the ground along the normal of the sand's collider, all in one mesh drawn with `Graphics.DrawMesh` for every camera
  (so each player of a split screen sees them); the boot's shape and its light are made by the shader, as a pit of three centimetres whose walls catch the sun or stay in shadow
  (`_DSSunDir`) and whose bottom is a little darker, multiplied over what is already on the screen: at low sun every print has a bright side and a dark one, at noon it hardly shows, at
  night it is only darker.
  `DesertDevils` makes **dust devils**: in the hot hours (sun above 25 degrees), with little wind and no storm, one column of whirling dust, 30 m tall, is born 160 to 420 m away in front of
  the player (never within 45 m of a place or 150 m of the cathedral), wanders for 70 to 110 seconds at walking pace, going round the places, and fades out; one particle system that
  follows a point on the ground (rising 6.5 m/s, orbiting 2.6 rad/s, widening with height). It does not push or hurt anybody.
- **Memory** (`DesertDirector.CleanTick`). The meshes and materials of a place are made while playing and belong to nobody, so destroying the
  place leaves them in memory until something sweeps the unused assets; Unity does that only when a scene loads, and here the scene never changes.
  The soak test (`-desertonly soak`) showed it: every round of four places left about 1,800 meshes, 360 materials and 120 MB behind, which an hour of
  walking would have turned into gigabytes. Now, 6 seconds after a place is unloaded, the director calls `Resources.UnloadUnusedAssets` (at most
  once every 60 seconds, and not with an enemy within 30 m: `CleanDelay`, `CleanInterval`, `CleanCalm`; only what nothing refers to goes). A sweep
  took 4 to 6 ms here and the log says how long each one took and the longest frame in it.

## The code

| File (`unity/PryingEyes/Assets/_PryingEyes/Scripts/`) | What it does |
| --- | --- |
| `Deserto/DesertMode.cs` | The state of the mode: active, seed, clock, what was taken and which bosses fell, the choice of the menu, danger by distance, the branch of a place. |
| `Deserto/DesertWorld.cs`, `DesertNoise.cs`, `DesertClock.cs`, `DesertWeather.cs` | The pure world (ground, regions, sites), the noise, the clock and the sky at an hour, the weather. Compiled by `tools/desertlab` too. |
| `Deserto/DesertTerrainGen.cs`, `DesertTerrain.cs` | The sand mesh as plain arrays (also in the lab, with the tint of the region in its vertex colours) and its streaming. |
| `Deserto/DesertScatterGen.cs`, `DesertScatter.cs`, `PropMeshes.cs`, `PropMeshes.Landmarks.cs`, `PropMeshes.Life.cs`, `Resources/Shaders/DesertProp.shader`, `Resources/Deserto/*.mat` | The things on the sand: where they are (pure, also in the lab), the instanced drawing and the colliders, the meshes made by code, the shader with wind and flames, the two instanced materials. |
| `Deserto/DesertLife.cs`, `DesertDust.cs`, `DesertSpindrift.cs`, `DesertFires.cs`, `DesertDevils.cs` | Tumbleweeds, animals and flocks; the sand that flies; the veils of sand off the dune crests; the lights, smoke and sparks of the camps; the dust devils. |
| `Deserto/DesertPrints.cs`, `Resources/Shaders/DesertPrint.shader` | The footprints on the sand: one mesh, the boot and its light made by the shader. |
| `Deserto/DesertSound.cs`, `Deserto/SoundDeserto.cs` | The hiss of the sand, the crickets, the vultures' caws, the howl and the crackle of the fires: what plays when, and the generators of the five sounds (`SoundGen`, `SoundBank`). |
| `Deserto/SiteFacade.cs`, `Proc/FloorBuilder.Facade.cs`, `Proc/FacadeKit.cs` | The outside of the places (roofs, cornices, windows, ruins) and their far skins. |
| `Deserto/Enemy.Desert.cs` | The enemy of the desert: rests, wakes, gives up. |
| `Deserto/DesertSky.cs`, `Resources/Shaders/DesertSand.shader`, `DesertSky.shader` | The sun and moon light, fog, exposure and the sky dome (stars, clouds, the sun's disc); the sand with its grain and ripples. |
| `Deserto/DesertGrade.cs` | The colour grade of the open desert by sky and weather (pure, also in the lab); `DesertSky` applies it and gives the room's look back indoors. |
| `Deserto/DesertNav.cs` | The sliding navmesh. |
| `Deserto/DesertDirector.cs`, `DesertSites.cs`, `DesertStairs.cs` | The director (places, walkers in the open, loot in the open, bosses, the edge of the world, the renewal of the Library, the sweep of unused assets, the compass words), a loaded place and the stairs of a stack. |
| `Deserto/DesertPickup.cs` | Points, ammunition, medikit and essence. |
| `Deserto/DesertCathedral.cs`, `Proc/RoomKits.HubDeserto.cs`, `Proc/HubPlan.cs` | The cathedral in the desert: plinth and stair (one stone mesh, physics in hidden boxes), the two braziers (a bowl, three legs and a ring in one mesh, `BrazierMesh`, built with `DesertLathe`) with their fire (`BrazierFire`), the two banners of the stair and those of the towers (`DesertBanner`), the lamp posts (`DesertLamps`) and the awnings of the stalls (`DesertAwning`), the armoury and its prices, the places of the two works. |
| `Proc/FloorDirector.Desert.cs`, `FloorDirector.Catacombe.cs` | Starting the game, the wait for the ground, the HUD and the objective, the fall guard, what a rebuilt cathedral must not wipe. |
| `GameMenu.Mode.cs` | The choice *Standard* / *Il deserto*. |
| `DesertTest.cs` | The in-game check (below). |

Changes elsewhere: `Stealth.Enabled` is off in the desert (no stealth: see `Enemy.Desert.cs`), `Enemy.FloorDirectorCeiling` and `MoveFlying` follow the ground, `WallBuy` has free
panels, `Catacombe.OnKill` takes the branch of a drop from the place, the standard gates of the descent are not built, `HUD` has a shade at the top
and outlined texts and a two-line objective while the desert is on, `ProcAmbience` and `PlayerController.Surface` give the wind, the dry echo and
the sand steps (`SoundGen.Step` surface 3), the menu's *Gioca* asks first (`GameMenu.Mode.cs`; `PadMenuTest` and `PresentationTest` choose
*Standard*), and `Shaders/ChurchHaze.shader` measures the height of its haze from the floor of the room instead of from the world's zero.

## Decisions that were left to the developer

Said here so they can be changed.

- **The boss's reward** is the existing trophy of its branch (`Fabbrica.Trofeo`), banked at once: a branch is drawn by the place.
- **"Scarce, common weapons"** was read as grade I panels, free; grade II in a boss's hall. Every other weapon is in the armoury.
- **"Twice the points"** is `RoomKits.ArmoryPriceMul = 2` on the standard prices.
- **The perk books and the consecration** are two works, **only in the desert**, built with the resources the places give; the rest of the
  cathedral and its works are unchanged.
- **The size of the world** is 18 km of radius (the distance a walker can reach in about an hour of play), not infinite; the edge is a toast and a
  push back inside, not a wall.
- **The cathedral is a sanctuary** (no navmesh in it, nothing flies over it).
- **The AI** is not stealth (nobody asked for stealth in the desert): the enemies rest until you come near, wake together and then hunt like the ones of the
  rounds; they give up if you get far. It is not the omniscient AI of the wave cycle either, which would send every enemy of the desert at you.
- **The world is livelier, not more dangerous**: the animals, the birds, the tumbleweeds and the weather hurt nobody; the camps are a place to find, not to
  fight (no enemy is placed there; one could be).
- **The Library renews** its three books 45 seconds after one is taken, and tries again when the number of perks changes (death takes one); building
  another work rebuilds the cathedral and so also draws three new books.
- **Sand quiets the steps** (0.8 of what marble sounds like to the monks), so that sneaking over dunes is a little easier than over tiles.
- **Death** uses the penalty of the floors and a respawn at the stair.
- **No map**: the cathedral has never had one (`M` does nothing there) and the desert does not add one; the objective names the nearest place and the
  way home instead. A map of the places already seen is the obvious next step if the compass words are not enough.
- **Not saved**: the desert, its seed, the clock, what you took. The cathedral, the store and the works are saved as always.
- **Not in the desert**: co-op, split screen (they stay standard).

## How to check

```powershell
dotnet run --project tools/desertlab -c Release            # the pure code, no Unity (a few seconds once built); add -- --quick for fewer samples
& $exe -batchmode -nographics -autotest -desertotest -seed 1000        # the real game, from the cathedral
& $exe -batchmode -autotest -desertotest -desertshots -seed 1000       # the same with a window and the photos of hotel_build/deserto
& $exe -batchmode -autotest -desertotest -desertonly varieta -desertshots -seed 1000   # one of the long phases (below): soak, varieta or prestazioni
```

Three more flags help when something fails: `-desertonly scalinata,cappella` runs only those phases (the arrival always runs; the others are
`cielo`, `cattedrale`, `opere`, `oggetti`, `suono`, `torre`, `boss`, `catacomba`, `notte`, `morte`, `lontano`, `caduta`, `foto`), `-desertnavlog` writes, for
every navmesh tile, how many sources it collected and how many triangles the navmesh has when it is done, and `-desertdiag` (with `-desertshots`) says why
a hall looks wrong: ambient light, fog, the six nearest lights, the nearest wall and what it is made of, how many renderers are around, and three more
photos (towards the wall, at the floor). With a window the test also measures the contrast of what the camera sees in the chapel, in every level of the
catacomb and in the two boss halls (`FrameSpread`: a fog or a black room gives almost none).

Seventeen phases are long (or are only photographs) and run only when asked by name (`-desertonly soak`, `varieta`, `prestazioni`, `costi`, `esterni`, `orizzonte`,
`paesaggio`, `nuvole`, `cose`, `galleria`, `rilievi`, `look`, `suolo`, `orme`, `creste`, `salina` or `vita`, one or more per run, separated by commas), since the default run would double in time. Most are the ones of the livelier desert,
to be run with `-desertshots` and looked at: **`nuvole`** (photos only) shoots the sky from the churchyard with three covers (`DesertSky.CloudOverride`: 0.12, 0.27 and 0.45) at noon, 16:24 and
17:54, once away from the sun and once into it: it is how the shape and the light of the clouds and the rays were judged; **`paesaggio`** (photos only) takes the first look from the churchyard at dawn, noon, dusk, night and in a storm and, from a
vantage point of the dunes, the badlands and the holy land (the highest spot of the area with a gentle slope, looking where the ground rises most), the landscape by day, with
the low sun in the face, at dusk and at night: it is how the sky, the light, the grade and the relief are judged; **`galleria`** (photos only) goes up to the nearest copy of
a dozen kinds of props (rocks of four tints, two boulders, the slabs, the cactus, the dead tree, the bush, a column, a stele, pebbles) and shoots each from the side of the
low sun at 16:12; **`rilievi`** (photos only) goes to the two buttes nearest the cathedral and takes each from 500 m on the side of the low sun (the profile on the horizon)
and from the rubble at its foot (the strata, the gullies); **`look`** (photos only) shoots the same view from a vantage point of the dunes at 07:18, 11:00, 15:30, with the
low sun in the face (17:24) and at 17:54, and puts the statics that tune the day back afterwards: it is how the light curve, the eye adaptation and the sun's halo were
chosen, by looking and not by guessing from numbers; **`suolo`** (photos only) goes to five open spots round the cathedral and, across the sun, takes the sand at a few metres in
the morning, at a few metres in the evening and the plain out to 70 m in the evening (the ripples, the pebbles, the grain, the cloud shadows); on two of the spots it also takes the sand at
a few metres in the evening and at noon with the pebble pavement forced on everywhere (the shader's global `_DSPebbleBoost`, 0 in the game: the noise that places the patches puts few of them where the
test happens to stand, and the stones must be seen); **`orme`** (six checks, three photos
with `-desertshots`) walks across the sun on open sand until the feet have made 16 prints, checks that every step leaves one, that all are still there, that the last lies by the
feet and on the ground, that a step in the air leaves none and that the wind has erased them all after 82 s, and shoots the trail from its end, the last prints from 2.4 m and the same
at 11:30 (when they hardly show); **`creste`** (three checks, three photos with `-desertshots`) goes to a vantage point of the dunes with the search for crests at eight times the speed
and checks that at least two crests start to smoke within 45 s, that there are live particles and that they are not too many, then shoots ahead, into the low sun and to the side; **`salina`** (three checks, eight photos with `-desertshots`) goes to the first salt flat, finds a flat spot in the middle of a lake and a dry shore with a lake ahead, checks that there are lakes and that a step in the water leaves no print while one on dry sand does, and shoots from the shore over the lake at dawn, noon, dusk and night, from the water towards the low sun at dawn and at dusk (the path of glints), at the stars and at the water's surface from 3.5 m; **`costi`** measures what each part of the picture costs in one place (see "The cost of a frame" below); **`esterni`** takes photos of the outside of one building of every kind and state, two towers, two catacombs and the chapel (four views from the
corners and one from above) and the cathedral from four sides; **`orizzonte`** waits, from three points, for every far skin within 760 m to be built, checks that
none stays lit where the real shell is complete and measures their weight, and photographs the horizon in four directions by day and at dusk; **`cose`**
goes to a point in the middle of each of the six regions and checks that the squares around have thousands of props of at least six kinds and the colliders,
with three photos each; **`vita`** checks the animals (they are there and one runs away at two metres), the tumbleweeds, the vultures over the landmark tower by
day and the bats at night, a camp (fire out by day, burning at night, tents), one of each landmark, and a sandstorm on demand (fog, sun, sand in the air,
the animals in shelter, and everything back afterwards), with photos of all of them. **`soak`** visits two far towers and two far catacombs three times, going back to the open sand in between, and checks that the number of
live meshes, materials and textures and the memory do not grow from one round to the next (and that the director's sweep of unused assets ran and did
not freeze the game); with a window it also looks at the sand and at the nave afterwards, to see that the sweep took nothing from the scene. **`varieta`**
(add `-desertshots` for the contrast checks and the photos) builds one place of every kind and state that the seed has near the cathedral (house, inn, church,
hotel, theatre, sanatorium, complex; whole, ruined, split, collapsing) and a second tower and catacomb, walks into each from the sand through the room that has
the door, then on foot to the room that is farthest from the entrance counting doors (the doorways of crumbled walls are gaps with no lintel, see "Outsides and far skins"
in "The world": a head cut to 1.5 m under a soffit once shut the navmesh agent and every enemy out of three of these buildings), and with a window checks the
contrast of the view; for the tower and the catacomb it also moves to the boss hall and checks that the boss appears and that the hall can be seen. **`prestazioni`** (needs a graphics card, the window stays hidden) renders the player's camera by hand at 1920x1080 into a
texture, waits for the GPU and takes the median of seven frames in seven places: the churchyard facing the sand, the open sand with the landmark tower by day
and by night, the foot of the tower looking up, the nave, inside the chapel and the boss hall of a catacomb; a place over 33 ms fails.

## What is verified

Everything below was run on 18 November 2026 on the developer's PC (seed 1000), on the working tree that was committed.

- **The pure code**: `tools/desertlab`, 98 checks, all pass (the list is in [../engineering/testing.md](../engineering/testing.md), section 2.4).
- **The real game**: `-desertotest`, 126 checks, all pass, in about three minutes without a window. In order: the game is in the desert and alone; the
  camera clears to a solid colour with a 1,500 m far plane (without it the scene's own sky is drawn over the dome); the cathedral is the usual plan with
  its armoury; the sand, the sky and the navmesh exist and the navmesh keeps every tile it has built; you start on the churchyard; no enemy in the
  cathedral; the stair has twelve steps of 20 cm, the sand at its foot is at the right height and does not cover the steps, you walk down it
  and the walkers get a navmesh on the sand but not on the stair or inside the cathedral; the two braziers at the head of the stair burn (four particle systems each) and stand on a tripod; the two banners hang from their bars and the wind moves their hem; an intact tower has a banner on a mast on its roof and one hanging on each long wall (four on the landmark), the wind moves them without ever pushing a wall banner into the wall, and a ruined tower has none;
  on the sand the props are sent to the card and the rocks far away have the reduced mesh; the clock starts at 07:00 and runs, noon is bright, midnight
  is dark with stars, the sun is low at dusk and under the horizon after it; the armoury sells every good weapon at twice the price and not the
  starting pistol; **the Library of Vows and the Altar of Consecration are built with the resources** (the exact payment), the Library offers three
  different books on three lecterns, one is bought for its price, the other two close and, after the renewal delay, three new books that you do not have
  stand on the lecterns; the altar is built after the vaults, the pistol is consecrated once for 3,000 points, the desert is intact afterwards, and both
  works hide when the mode is off and come back with it; the sand makes sand steps (clips of 0.15 to 0.3 s), the open air has wind and no hum, no organ
  and a short echo, the nave has its long one; the objective names the nearest place and the way home with the right side (ahead when you face the
  cathedral, behind when you face away); the welcome chapel is where it should be, is built when you come near, is furnished, is entered on foot and has
  things to take; a coin, a crate and a free weapon are taken by walking over them and do not come back; the landmark tower has eight floors here and is 43 m
  high (towers have 7 to 14), you walk in and climb its stair on foot to the first floor, and its navmesh joins the floors; the boss is on the top floor, appears, falls and
  its trophy goes to the store; the nearest catacomb (4 levels here, 3 to 5 in general) has a well in the sand, you go in from the sand, walk down its stairs to 13.6 m under the
  entrance, reach the boss hall, the props of the sand are not sent to the card from down there, the boss appears and its trophy goes to the store; at night enemies wander on the open sand, not too many; loot is born
  around you; **death** takes half the points and the last perk, wakes you at the top of the stair and leaves the same seed, sand, sky and a running
  clock; at **5, 9 and 17 km** the sand holds, the navmesh is under your feet and the building next to you loads with every level, and past 18 km you
  are put back inside; whoever falls under the sand is brought back; no frame longer than 1.2 s and no error in the log.
- **With a window** (`-desertshots`, 45 checks of the halls: the chapel, the tower, the catacomb): the contrast of what the camera sees is 0.26 to 0.78 in
  the chapel (0.78), in the tower's boss hall (0.71), in the entrance hall of the catacomb (0.26, the darkest of all), in each of its three levels down to -14 m
  (0.40 to 0.49) and in its boss hall (0.68); a solid fog or a black room gives under 0.05. This check exists because of a bug that no other check saw: the haze of
  the church naves grew with the world height, so in the catacombs, below zero, every nave was a wall of grey and the boss hall a uniform blue picture; the
  photos were taken and the checks were green. Photos: 13 of the same run (arrival, dawn, noon, dusk, night, the chapel inside, the tower from far and from its
  foot, its first floor and its boss hall, a catacomb entrance and its boss hall), plus one per catacomb level and three of the tower's banners (from the entrance side, the cathedral side and the east), looked at one by one.
- **The memory** (`-desertonly soak`, 9 checks, with a window). Three rounds of two far towers and two far catacombs, going home in between: live
  meshes 1,709, 1,714 and 1,715, materials 994, 1,032 and 1,032 (the caches of the facade palettes fill up in the first rounds and then stop), textures 419
  each time, memory 696, 721 and 737 MB. Before the sweep of unused assets was added the same test failed with 3,761, 5,545 and 7,317 meshes, 824, 1,328 and 1,688
  materials and 711, 860 and 983 MB: about 120 MB left behind by every four places. Four sweeps in the three rounds, the longest frame 1 ms and the last
  sweep 6 ms; after them the sand and the nave still have their contrast (0.55 and 0.51).
- **Every kind and state of place** (`-desertonly varieta -desertshots`, 40 checks). Eight buildings near the cathedral, seven kinds in four states
  (whole, ruined, split, collapsing; from a one-room house to a 24-room sanatorium): each is built, furnished, entered on foot from the sand through its door,
  walked through to the room that is farthest from the entrance (up to five doors away) and has a view with contrast (0.62 to 0.93 of the luminance range);
  a tower of 9 levels (entrance hall 0.48, boss hall 0.62) and a catacomb of 5 (entrance hall 0.25, boss hall 0.64): the shell of every level, the walk in from
  the sand, the boss in its hall. Three of the places of an earlier run (a ruined complex, a collapsed inn, a collapsed hotel) could not be entered at all:
  the doors of their crumbled walls were 1.5 m slots under a slab of ceiling; that is what `Opening.Gap` and the check `BUILDING_LOW_DOORWAYS_ARE_GAPS` of the
  lab are for. The photos of the theatre and of the tower's hall were looked at.
- **The cost of a frame** (`-desertonly prestazioni`, 15 checks) on an RTX 5060 Ti at 1920x1080: 7.5 ms on the churchyard facing the sand, 9.2 ms (day) and
  9.3 ms (night) on the open sand with the tower in the distance, 6.8 ms at the foot of the tower looking up, 5.0 ms in the nave, 7.0 ms in the chapel, 7.0 ms
  in the boss hall of a catacomb, 5.5 to 7.0 ms on the open sand of each of the six regions, 6.6 ms at a night camp with its fire and 6.9 ms in a sandstorm: the
  worst place costs 9.3 ms, far from 33 ms. The tripod braziers, the banners, the lamp posts and the awnings of this round cost nothing that the measure can see (the worst place was 9.5 ms in the run before and the
  difference is inside the noise of about a millisecond between two runs). The heavier rocks and the new pebbles cost something, and it was measured: with the heavier rocks alone (every rock at all its triangles out to 280 m, every
  boulder out to 520 m, in every shadow map) the open sand was at 9.3 ms and the boss hall of a catacomb at 14.7 ms (11.1 ms before the rocks); the two versions of the rocks and the
  cut under the sand brought the catacomb to 7.7 ms and, in the run after the cacti and the bushes got their two versions as well, to 6.6 (the cut leaves almost nothing of the props to draw there, so the
  difference between those two is the noise of the measure: about a millisecond). `costi` (a measure, no check) takes each part away in turn, and the same part measured in two runs
  differs by about a millisecond, sometimes more. In the last run: at the churchyard (7.5 ms) the sun's shadows are 1.5 ms, the sand itself 1.9, the props on the sand 1.1, the far places 0.8, and the point
  lights, the life, the particles and the post-processing have nothing to show beyond the noise (the sand alone has measured 1.6, 2.5, 1.2 and 1.9 ms in four runs with the same code: the claim that the sand's shader got heavier was not shown); on the open sand of the dunes (5.7 ms) the props are 1.4,
  the sand 1.9 and the shadows 1.0; in the boss hall of a catacomb (7.6 ms) the point lights are 1.4 and the props on the sand nothing (the cut under the sand); in the chapel (10.0 ms with 97 lights, and 7.0 ms in `prestazioni`: the two measures of the same
  room disagree and why was not looked for) the point lights are 3.3, the props on the sand, that can be seen only through the windows, 2.1 and the sun's shadows 1.2.
- **Footprints, crests, salt flats** (`orme` 6 checks, `creste` 3, `salina` 5, with a window and photos): every step leaves a print (16 prints in 5.4 s over 21 m), a
  step in the air leaves none and the wind has erased them all after a minute and a half; at least two crests start to smoke and the particles alive are many but
  not too many (83); the salt flats have lakes, a step in the water leaves no print and one on dry sand does. The photos of the trail, of the crests and of the
  lakes at dawn, noon, dusk and night were looked at.
- **The rest of the game**: `-cattedraletest` (the cathedral of the standard mode, built by the same hub file that the desert changed for its lamp posts and its awnings) was run on this tree, 72 of 72. The
  other tests were last run on the tree of an earlier commit (`dca7799`), and since then the files shared with the standard mode changed only in the desert branch of the hub, in a constant that kept its
  value, in one facade tint and in the awning and the lamp post (behind `DesertMode.Active`), so they were not run again: `-chartest` 23 of 23, `-survivaltest` 91 of 91, `-padtest` 89 of
  89 (it goes through the mode panel), the split screen with two players `-seatstest` 89 of 89, three co-op scenarios (a, c, i: 62 and 49, 11 and 7, 66 and 53
  checks), `MenuReview` 2874 of 2874, the four rule labs of the CI gate and the Python tests (see [../engineering/testing.md](../engineering/testing.md),
  [display-and-menu.md](display-and-menu.md) and [controller.md](controller.md)).
- **What the pictures found and the checks had not** (the third round of improvements, judged by looking at the photos of every phase): a black beam 14 m long that
  crossed the sky in every photo taken from the churchyard since the first day. The wide door of the churchyard was 3 m high on a parapet of 1.55 m, so its soffit and
  its two jambs hung in the air above the stair; nothing measured it. It is `Opening.ParapetTop` now (the opening is a gap in the parapet, with no lintel) and the lab has three checks that
  fail with the old height (tried): `HUB_PLANS_VALID`, `HUB_EXIT_IS_A_GAP_IN_THE_PARAPET` and `OPEN_ROOMS_OPEN_ONTO_THE_VOID_UNDER_THE_PARAPET`. The same round gave the
  pebbles their roundness and their tints (they read as oil stains), the plinth and the stair their stone, the braziers their fire and their iron bowl, the clouds their
  edges and their lining of light, the rocks and the bushes their shape, and the sand at the foot of each building its props (the yards). Each change was photographed before and after.
- **The fifth round** (the same request, judged by photos): the braziers of the churchyard were a bowl on a stem that read in the pictures as a goblet and their fire as a candle; they are now a wide
  shallow iron bowl on a tripod held by a ring, with more fire, and the test measures them (a tripod and not a stem, wider than 1.2 m at the foot, 1 m high); at dusk their smoke goes brown with the light.
  The stair got its two banners, and the lamp posts and the stalls of the market their own desert models (a six-sided lantern with its glass, a striped awning with a scalloped valance).
  The banner of the tower is the one the checks had passed and the photos had not shown: every number said it was there (position, size, drawn, its hem moving 26 cm) and from 90 m the photo
  showed only a brass finial. The cause was perspective, not the code: a camera at the height of a person looks up at a roof 43 m high over its parapet, and the line of sight over the near
  edge climbs past everything lying behind it (the banner stood in the middle of a wing's roof, 10 m behind the edge, so the parapet hid about half of the cloth in the photo). The answer was to hang
  banners down the walls, where they are seen (three photos, `13_torre_stendardo`, `13b_torre_stendardi_nord`, `13c_torre_stendardi_est`, from the entrance side, the cathedral side and the east), and to
  keep the mast, moved to the middle of the largest roof, for the horizon.
- **Two checks that were fragile and were made robust**: the climb of the tower's stair failed once because a Monaco woken by the test stood on the first
  ramp (the test now removes the guards around the player while it walks, as the catacomb phase already did), and `creste` asked for two smoking crests in 25 s
  from a random search that gave 2 in one run and 1 in the next at the same wind (now eight times faster and 45 s).

What is **not** verified:

- Nobody has played it: the feel, the difficulty, the pace and the balance (prices, danger by distance, how much there is to find, the strength of the
  bosses, how long a day should be) are guesses. The sound (steps, wind, echo) was measured, never listened to.
- Only this PC was measured (one graphics card, a window in some runs and none in the others), and only with the seed 1000: the lab checks the generators
  on hundreds of seeds, but the game itself was run on one world. The soak test is three rounds of four places (minutes), not an hour of walking, and the
  far places (5 to 17 km) are reached by teleport, so the streaming along a long walk is known only up to a few hundred metres. The cost of a frame is a
  manual render of the camera without the HUD, not a game played: real frames cost a little more.
- No controller in the desert, no multi-monitor, no co-op or split screen (they stay standard).
- Of the places in the open the test walks in and looks at one of each kind and state, a second tower and a second catacomb, but it climbs the stairs only
  of the landmark tower (to its first floor) and of the nearest catacomb (all the way down); the boss halls of the other two are reached by teleport, and the
  rooms between the entrance and the boss hall of the other towers have not been seen.
