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
outsides and far silhouettes of the places, the things on the sand, the weather, the wildlife and the camps below. The mode is part of release 0.9.0 (19 November
2026), together with the characters and the saves of [saves-and-characters.md](saves-and-characters.md). Release 0.9.1 (19 November 2026) repaired the walls of the places the day after
(the player saw the inside of some buildings from the sand and walls that flickered up close): see "The walls of the near shell, measured" below. The same release added a frame counter
(on in every mode, see [display-and-menu.md](display-and-menu.md)) and, at the player's request, **co-op in the desert** (up to four players), **a minimap** and **an arrow home to the cathedral**:
see "Co-op, the minimap and the way home (0.9.1)" below.

This page says how it plays, what is found where, how the world is made, the code, the decisions that were left to the developer, and what is
verified. The cathedral, the works, the resources and the satchel are those of [cathedral-and-catacombs.md](cathedral-and-catacombs.md); the
rooms are those of the floors ([tower.md](tower.md)).

## How it plays

1. *Gioca* (and *Ricomincia dalla cattedrale*) opens **Come vuoi giocare?**: **Standard** or **Il deserto**. The choice is remembered for the
   next time (`PlayerPrefs` key `PryingEyes.GameMode`; the automated tests never write it). The command line `-deserto` forces the desert.
   Since 0.9.1 the desert can also be played **in co-op, up to four**: *Ospita una partita* opens the same choice (*Che partita ospiti?*), the host picks, and the guests start where
   the host plays (they do not choose). It can also be played in **split screen**, two to four players on one PC over one or several monitors: after *Chi gioca?* the same choice comes
   (*Che partita giocate?*) and holds for every seat (see *Split screen* below).
2. You wake on the churchyard (*il sagrato*) of the cathedral, at **07:00**. The cathedral stands on a plinth of the same stone as the church facades
   (a darker base under a cornice, a lighter course at the foot); from a 14 m gap in the churchyard's parapet a wide stair of twelve steps goes down to the sand
   (a stone riser and a lighter slab with a nosing for each step, a sloping wall with a coping on each side and a pier at the foot), and a brazier burns on
   each side at the top (a wide shallow bowl of cast iron on a tripod held by a ring, with the coals inside): flames, a hot core, smoke and embers. The wall that closes the churchyard has a turret at
   each of its four corners (a box of the same stone with a cornice, a pyramid roof of slate and a slit window on its outward faces; they are solid) and from the sand the cathedral has the outline of a castle. Two banners of crimson cloth with the
   golden eye of the game hang from iron poles beside the stair and the wind moves them; the lamp posts (turned iron, a six-sided lantern with its glass) and the stalls of the market (a counter of warm brown wood, a striped
   awning with a scalloped valance and a drape of the same cloth hanging from the edge of the counter) are made for the desert too. Nothing attacks in the cathedral: the walkers have no navmesh there (they stay at the foot of
   the stair) and whatever flies over the cathedral disappears.
3. **A day lasts twenty minutes** (about twelve of daylight, eight of night; `DesertClock`). The sun, the moon, the stars, the fog and the
   exposure follow the clock; indoors (anything within 12 m above the head) the look goes back to that of the rooms. The top of the screen
   counts the days, and the objective is on two lines: first the hour, the phase (dawn, day, dusk, night), the nearest place and, when you are
   more than 60 m from the cathedral, the way home, each with the side it lies on as seen from where you look (*davanti*, *a destra*, *dietro a
   sinistra*...: sectors of 45 degrees, `DesertWorld.Sector`); then, dimmer, what to look for. Since 0.9.1 there is also a **minimap** at the top right and, from 90 m
   out, a golden **arrow** at the top centre that points at the cathedral and says how far it is (below).
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
    come near (6.5 m, 3.8 for the scorpions), **tumbleweeds** roll downwind (a tangled ball of pale grey straw: 40 stems that curve round a dark core, 32 small clumps of dry leaves on the shell and 70 spikes sticking out, about 4,000
    triangles. They used to be ten thin rings and 22 twigs, and in the photos a ball of barbed wire with the sky showing through it; the first rebuild had a few big brown clumps and from 7 m it was a boulder), **vultures** circle over the ruins and the towers (up to five flocks within
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
- **The walls of the near shell, measured** (0.9.1: `FloorBuilder.WallSide`, `WallCap`, `SharedCap`, `FloorBuilder.Facade.cs`, `MeshBuilder.Slab`, `Deserto/DesertCensus.cs`). The day after 0.9.0 the player wrote that "the missing pieces of the structures are
  broken and from outside you can see the inside of some buildings", and then that "some walls glitch up close". The first diagnosis was by eye and found only part of it, so the check became a probe that
  needs no graphics card (`-desertonly rovine`, see "How to check"): it builds the shell of 33 places twice, with the walls of 0.9.0 (`FloorBuilder.LegacyWalls`, a static compiled out of the
  distribution builds) and with the new ones, and counts on the real triangles of each: faces whose winding is the opposite of their normals (**flipped**), pairs of faces of different materials on
  the same plane facing the same way that somebody can see (**fighting**: the pixels flicker; the back faces of two trims leaning on the skin of a wall face into the wall and are not counted, and a
  point of view in the air in front of the pair decides), rays from the sand at eye height whose first surface is seen from behind (**holes**: the eye goes through, and sees inside), and window panes seen from behind. What it found, all in the
  near shell (the far skins were right), and what was done:
  1. *The outer skin of a wall had no breaks in its profile.* A gable end, the arch of a vault end and the teeth of a broken wall were drawn as a straight line from end to end on the outside, so the
     triangle between the line and the real profile was a window onto the inside (the inside face followed the profile). The skin now gets the breaks (`WallSide`).
  2. *Flipped slices.* Where the top profile of a wall cuts the head of a door or a window, the slab above it was a bow-tie of two triangles, one of them upside down (624 in 33 places).
     `MeshBuilder.Slab` clips it where the two edges cross.
  3. *A slit at every outward corner.* The two skins that meet at a corner stop on the line of the wall and their planes are half a thickness (15 cm) off it: a slit from the ground to the roof. The skins are extended by
     half a thickness at the corners that no room covers (`ConvexCorner`, the same three-point test as the turrets).
  4. *Roofs with one face.* Pitches, vaults, spires and the pyramids of the turrets are looked at from below as well, and were see-through: each now has its underside.
  5. *Panes that look only inward.* From outside a window was a hole (950 rays in the 33 places went through one): the outer pane is the same quad facing out (`Glass`, `Pane`).
  6. *The wall above a lower neighbour started at the wrong height.* `StepRun` began at the lowest possible top of the neighbour (its wall height, or the lowest tooth of a crumbled one), so over a gable end, a vault or a
     jagged wall it lay on the face the neighbour has inside its own room, on the same plane and facing the same way (the two fight, the biggest source of flicker: 10,000 pairs), or left a hole between the teeth. It now begins on the profile of the
     neighbour and, if it has a roof, 12 cm above it.
  7. *Tops with no lid.* A broken wall (and the partition between two ruined rooms) is two faces 30 cm apart with nothing between them at the top: from a dune or a tower you looked down into the gap, to the sand. `WallCap` and `SharedCap` close it
     at the height of the room's own profile.
  8. *Trims taller than the wall.* On a crumbled wall the frames of doors and windows, the pilasters and the buttresses were as tall as the wall at its start, and stood in the air over a lower tooth: each is now limited to the wall
     at its place (`MinTop`, `PillarTop`).
  9. *Parapets and cornices flush with the inside.* The boxes that sit in the thickness of a wall (the parapet and merlons of a flat roof, the crown of a roofless wall) had their inner face exactly on the inner face of the wall, a strip along the top of every
     flat-roofed wall: they now stop 6 mm short (`InnerFlush`).

  The numbers, with seed 1000, in the same 33 places (`rovine TOTALE` in the log): **flipped faces 624 to 0; fighting faces of a facade 18,884 to 334; rays that see through a wall 1,578 to 506 of 79,200; panes seen from behind 949 to 3.** They are not zero, and
  the page says what the rest is. The holes that remain are rays that enter by a door, a broken window or a collapse of a ruin and see the back of an inner face (a ruin is open, that is not a defect), and, on the partitions of two ruined rooms,
  a band along the top where the taller of the two faces is not seen from the side of the lower one. Of the fights, about 1,500 are *inside* the rooms and were left alone: in the towers the top of the ceiling beams is on the plane of the floor of the level above (`FlatBeams` puts it at the ceiling height, and the levels
  are stacked with no slab between them) and a crown moulding is flush with a ceiling; they are the same in 0.9.0 and in the standard game. The checks are guards against going back, not a claim of zero: see "How to check". The probe draws, with the CPU, what an eye
  sees (`-rovinepng`, `-rovinematch`, `-rovineboth`; magenta where it sees into a wall, yellow into a pane): those images were looked at, and they are flat colours, not the game: **no photo with the graphics card was taken, because the developer's own game was running on the same PC.**
- **The banners of an intact tower** (`DesertBanner.OnTower` and `OnTowerWalls`, made by `SiteFacade` with the last piece of the far skin and kept when the real shell takes its place;
  a ruined tower has none). A mast with a banner twice the size of those of the stair stands in the middle of the largest flat roof of the top floor (the beamed ceiling inside is
  a flat slab from outside), and under the cornice a **banner hangs down each long wall**: one per side, on the longest stretch of outer wall of the top floor if it is at least 7.5 m (up to four; 4.2 m
  wide, 11 m long, on an iron bar held 96 cm out from the wall by two arms). The first version had only the mast, and the photos showed why that was not enough: a roof 43 m up is not seen from
  the ground nearby, the line of sight over the parapet hides the cloth from anyone within about 150 m (from 90 m only the finial showed, and what looked like a tan block at the corner of the
  crown was the cloth, cut by the parapet). The wall banners are seen from every side and close up, the mast from far away. The wind moves them all (`DesertSky.Weather`: strength, gusts,
  direction), but a wall banner is never pushed into the wall: its displacement toward the wall is cut to a quarter and stops 45 cm out, so the cloth stays at least 35 cm from the facade (80 cm at rest; the cornice juts 22 cm).
  That bound holds by construction; the test measured the nearest it came in the light wind it met (46 cm at the tower's banners, when the limit is 25).
- **The corner turrets of a tower** (`FloorBuilder.Turrets`, switched on for the top floor of a tower by `FloorBuilder.FacadeTurrets`, in the real shell and in the far skin). A tower was a box with
  battlements and, from the horizon, no outline at all. At every outward corner of a flat roof of the top floor there is now a stone box 2.8 to 3.4 m wide and 3.2 to 4.4 m tall (it juts 25 cm out of the
  face of the walls and has a bottom, so it is seen from below), a cornice, a pyramid roof of slate 2.6 to 3.6 m high and, in the near shell only, an iron finial; each of its two outward faces has a dark slit window
  (three in ten are lit at night). A corner is outward when no room stands in front of it nor beside either of the two walls that meet there (three points a decimetre outside are looked at: `Covered`),
  and a room narrower than 6 m has none. The sizes and the slits come from the room and the corner, so two towers do not have the same turrets. The check asks that the tallest facade of the tower stands more
  than 5 m above the roof (the battlements are 1.7 m); the photo `13d_torre_torretta` is the view from the foot. The churchyard of the cathedral has the same four
  (`DesertCathedral.Build` calls `FloorBuilder.Turrets` for the churchyard, standing on its floor, with a fixed side of 2.6 m so that they do not touch the stalls, which stand 3.2 m from the wall, and with hidden box
  colliders that the function hands back): before them, from the sand, the cathedral was a low enclosure with battlements and nothing above it (`02b_cattedrale_dalla_sabbia`, before and after).
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

## Co-op, the minimap and the way home (0.9.1)

The player asked for all three the day after 0.9.0 ("co-op, the minimap and the arrow to go back to the cathedral"). They share a rule with the rest of the desert: nothing about the world
is sent over the wire but the seed, because the world is a pure function of it.

**The minimap** (`Deserto/DesertMapGen.cs`, `DesertMapTiles.cs`, `HUD.DesertMap.cs`; `N` shows and hides it, as in the floors; `M` still does nothing in the desert). It sits at the top
right, a window of 512 m of sand centred on you, north up, and it is a cartographer's chart: parchment-coloured sand shaded as if lit from the north-west, contour lines every 3 m (stronger
every 15), hatched cliffs, the pale pans of the salt flats, the footprints of the rooms of every place with their walls in ink (filled if they have a roof, hatched if they are open to the sky),
a cross on the chapels and churches, a diamond on the towers, an arc on the catacombs, and the cathedral with its golden stair and the circle (42 m) where no enemy is born. Over it the HUD
draws, every frame, **you** (a red arrow that turns with the view), **your companions** (in the colour of their seat, a red ring that pulses if they are down), the **awake enemies within 90 m**
(red dots, a dark ring for a boss; whoever rests is not a threat and is not drawn), your own **parked bike** (a turquoise disc: see "The motorbike") and, when the cathedral is outside the window, a **golden arrow on the edge** that says where it is. At night the
chart turns blue and dark; underground (the catacombs) it is dimmer still. The painter is pure (`DesertMapGen`: a 64 m square is 32 x 32 pixels, 2 m each, the same square always comes out
the same, so neighbours meet without seams, and `tools/desertlab` compiles it and can draw it to an image); the chart is a wrapping texture of 10 x 10 squares that the window reads
(`DesertMapTiles`: a square lives in the slot `tile mod 10`, so walking only replaces the squares left behind; three painting jobs at a time on worker threads, 2 to 3 ms each, and at most four
uploads per frame on the main thread; a square is painted only when the places round it are known). Beyond the painted squares there is the dark paper of the unexplored. It is a
nice-to-have: if its code throws three times it switches itself off and the rest of the HUD goes on.

**The way home** (`Deserto/DesertHome.cs`). From 90 m from the cathedral's perimeter a golden arrow appears at the top centre of the screen (the one the floors use for the lift, `HUD.SetGuideArrow`,
driven by `Guide.TickArrow`), turns to point at the cathedral relative to where you look and says how far (*cattedrale · 1,4 km*: tens of metres under 300, fifties up to a kilometre, then
kilometres with one digit). It is a straight line, because on the sand there are no roads to follow, and it goes off when you are down, dead or in a transition. The minimap shows the same bearing
on its edge.

**Co-op** (up to four; the split screen is the next section). The host picks the mode in the menu (*Ospita una partita* opens *Che partita ospiti?*, `GameMenu.Mode.cs`) and the guests go where the
host plays: `Welcome` carries a flag for the desert, the guest builds its own desert from the host's seed (`FloorDirector.StartRun(coopDesert)`), and it uses the same handshake as the floors,
with the first epoch (`Coop.OnFloorStart(0)` at the start, `OnFloorBuilt()` once the sand round the arrival is ready, `FloorReady` back). Everybody arrives on the churchyard, 0.9 m apart by slot.
What is **everybody's**: the enemies (the host simulates them all and the guests see puppets), the hour of the day, the places, the boss that falls. What is **each player's own**, as in the
floors: points, weapons, perks, the store and the cathedral, the loot on the sand (every machine keeps its own list of what it took) and the trophies. How it works (`Coop/CoopGame.World.cs`,
`Deserto/DesertAnchors.cs`):

- **The host builds round every player.** The sand with its collider, the navmesh tiles, the places with their guards and the props' colliders are made on the host not only round its own
  player but round each living companion that has built the desert (`DesertAnchors`, the feet of the companions, once per frame), so whoever chases a companion who is a kilometre away walks on
  sand, on navmesh and past walls. The guests are passive: no wandering enemies, no guards, no waking and no boss to watch on their side (the boss's trophy comes by message). Wandering enemies are born
  round one player at a time, in turns, and their target and the cap on the sand grow by 70 % for every companion; an enemy wakes for the nearest player and gives up only if it has lost them all.
  (A bug found by the co-op test: a sand block that the host's own view had already made at the low level of detail, without a collider, was never raised when a companion came near,
  because the hysteresis of the host's view said to keep it; the sand under the companion had no collider and no navmesh. `DesertTerrain.Plan` now raises it whatever the host's hysteresis says.)
- **Each guest sees the enemies near it.** Over a kilometre of desert a guest cannot have every puppet of the host: the host keeps, for each guest, the set of the enemies it has sent (`known`) and
  every 0.3 s sends the ones that came within **190 m** and quietly takes away (a removal, not a death) the ones that went beyond **230 m**; attacks, shouts, bolts and the death of an enemy go only to whoever has
  its puppet; the poses (10 times a second) only for the known ones. A guest that finishes building receives at once the enemies round it.
- **The clock is the host's**: every two seconds (unreliably) the hour and the day; the guest's own clock keeps going in between, and is put right.
- **A boss that falls falls for everybody** (`DesertBossDown`, reliable; the host keeps the list for whoever arrives late): every player's store receives the trophy of the branch, the place does not put
  the boss back, and the banner and the roar are heard by all.
- **Who is asleep.** `Enemy.dormant` travels in a bit of `EnemyNet.status` (`Enemy.DormantBit`, 0x80; the magic and seal states use the low six), so that a companion's minimap does not show as a threat
  an enemy that is resting.

Protocol `pe-coop-31` (the `Welcome` flag, `DesertBossDown`, `DesertClock`, the bit of `status`); the motorbike raised it to `pe-coop-32` (see "The motorbike"). The scenario `j_deserto` of `tools/coop/run_all.ps1` tests it with two processes (below).

**Split screen** (two to four players on one PC, over one or several monitors, 0.9.1; [../multiplayer/split-screen.md](../multiplayer/split-screen.md) has the rest). One process, one desert, one
`Seat` per player (`Coop/Seats.cs`). After *Chi gioca?* (the characters) the menu asks *Che partita giocate?* (`GameMenu.Mode.cs`, the panel of the single player) and `SplitBegin` starts the run;
`FloorDirector.DesertWanted` no longer refuses a split screen and `StartDesertCo` begins one expedition for each seat (`BeginSeatExpeditions`: the cathedral, the store, the points, the perks and the equipment of
its character, as in the floors). Everybody arrives on the churchyard 0.36 m apart and then walks where they want: one seat can be kilometres from another. What each seat has **of its own**: its camera (and the part of
the monitor the split screen gives it), its HUD (the objective line with the place nearest to *that seat* and the way home, the minimap with the other seats as coloured dots, the golden arrow, the points), what it picks
up from the sand (a pick-up is taken by whoever comes within reach, and the reward is that seat's), the edge of the world and its falls. What the seats **share**: the enemies (each targets the nearest seat), the hour
of the day and the weather, the places, the bosses, and the one audio listener (seat 1's). How it works (`Deserto/DesertAnchors.cs` and the files named):

- **The world is made round every seat and seen from every seat.** `DesertAnchors` lists the other seats beside the companions of a co-op game and marks them as *viewers* (`ViewerPositions`): a companion across the network is
  *served* (sand with a collider, navmesh, places and guards on the host) and not looked at, while a seat is also *looked at*, so round it the sand goes out to the horizon (`DesertTerrain.Plan`: a viewer has the reach of the
  player's own, and the sweep keeps what any of them needs), the props are drawn and given colliders (`DesertScatter`: cells, level of detail and fires are chosen by the nearest of the viewers, one draw per kind over the union),
  the far skins of the places are made for each (`DesertDirector.PlanFar`) and the loot lies on the sand round each (`OutdoorLoot`, thrown away only when far from all).
- **One sky, a dome for each camera.** The fog, the ambient and the colour grading are global, so the look is one: outdoors if *any* seat is outdoors, indoors only when all are. Every seat's camera gets its own solid background and
  its 1,500 m far plane (`DesertSky.AttachExtra`, restored when the sky is destroyed); the dome is a single object that moves to the camera that is about to render (`RenderPipelineManager.beginCameraRendering`), and the fog colour toward the sun
  is set there too for that camera's heading (`TintFog`). `DesertSky.IndoorFor(camera)` says how indoors one seat is.
- **Effects round each seat.** The flying sand, the weeds and animals, the veils off the crests and the dust devils exist once per seat (`DesertDirector.Create` makes the instances of seats 2 to 4, each with its camera and
  its own random seed; `I` stays the first one's); the fires are one component that lights the four nearest fires of each seat in turn (`DesertFires.PickNear`).
- **Per seat and not per machine**: the objective line (`DesertDirector.WhereText` keeps its cache for each seat and `RefreshObjective` writes the lines from inside each seat), the edge of the world, the pick-ups
  (`DesertPickup`), the minimap markers (`HUD.DesertMap`) and the falls (`DesertFallGuard` already was).

## The motorbike

Request of 19 November 2026, the day of release 0.9.1: *a motorbike near the cathedral, to get around*. The desert is 18 km wide and 400 m of it take a walker a minute: a bike that is already there, at the foot of the stair, is
the first way for a player to cross the sand without the long walk. It exists only in the desert.

**How it plays.**

- A bike stands on its kickstand at the foot of the stair, a few metres south of the churchyard, pointing at the open sand: one for each player (`DesertBikes`; in a split screen one for each seat, side by side 1.8 m apart; in
  co-op the one of the player's own slot). Look at it and the use prompt says *Sali in moto*: **F**, the key of every use (doors, books, the revival of a mate), puts you in the saddle.
- **W** gas, **S** brake and, kept down, reverse; **A**/**D** steer; **Shift** turbo; **Space** handbrake (the rear slides: it is how a tight bend is taken); the mouse looks freely up to 100° each side, and after a second and a bit
  without looking the view comes back to the front. **F** gets off: at once under 4 m/s (14 km/h), otherwise the bike brakes by itself and the rider steps off to its side as soon as it is under 3.5 m/s. On a pad the same actions as
  walking are used (move, jump = handbrake, sprint = turbo, use); this was not tried with a real controller.
- Top speed 23 m/s (83 km/h), turbo 31 m/s (112 km/h), reverse 5 m/s. The gas gives 50 km/h in 1.8 s (measured), the brake takes 17 m/s² (a stop from top speed in under two seconds and about 16 m); at speed the field of view opens by up to 17°, the view
  follows half of the pitch and roll of the bike, bumps and landings shake it, and the sand rises behind the rear wheel. The rider is always seen from the first person (the third-person view is not used on the bike) and the bike
  is the whole of your hands: the weapon, the knife, the equipment and the magic are off until you get off.
- A bike is **loud**: while it moves (over 1.5 m/s) the engine emits, every 0.45 s, a noise (`Noise.Emit`, kind `Other`) of 7 m plus 0.85 m for each m/s of speed, up to 26 m, farther than any footstep; at a standstill it idles and makes none.
- A wall taken at more than 8 m/s is a **crash** (the speed along the heading drops by more than 8 m/s in one hard stop: the losses of a braking harder than 60 m/s², three times the handbrake's, are added up over a tenth of a second, because the body touches a wall half-way through
  a frame and stops in the next, and counted one frame at a time the crash was missed now and then at 50 km/h (the wall check of the `moto` phase failed in two of the three runs in which the `minimappa` phase had run first) and in three runs out of six at 36 km/h, measured by `motourto`): the bike stops, the camera is thrown, a crack and a cloud of sand, and the rider is hurt for `(loss - 8) * 2.4`, at most 45 and nothing at all under 3 points (never
  enough to kill at full health in one go), with 0.8 s before the next. A landing from more than 6.5 m/s downwards thumps, from more than 15 m/s it hurts too. Being hit by an enemy, going down and dying work as on foot; going down or dying throws the rider off.
- **At night, and indoors, the headlight comes on** (a spot light, only while somebody rides: when the sky's `Night` is above 0.28 or its `Indoor` above 0.5) and the tail light shows the brake. The bike goes into the cathedral (`motocattedrale`): up the stair, through the portal into the narthex, where its roof lights the headlight even at noon, and through the arch into the nave, which has no roof until the *Volte* are built (the headlight stays off by day) and is shut at about z 36 by the building site of the Volte, whose boards stop the bike as they stop anyone. Nothing stops it from going into a building of the desert either (not tried).
- **The minimap (N) marks your own bike when it is parked**: a turquoise disc where it stands, a colour no other mark has (red for you and the enemies, gold for the cathedral, the seat colours for the mates). In the saddle it goes out (you
  are the red arrow); outside the window (256 m each way) it sits on the edge, on the side where the bike is, unless it would land under the golden arrow (a bike still at its place, next to the cathedral, is already told by the arrow). Only your own:
  in a split screen each seat has the disc of its own bike, and a mate's parked bike is not in your world, so it is not on your map either (`DesertBikes.Own`, `HUD.DesertMap.cs`).
- **The sound** is made by code like the others (`SoundMoto.cs`): a 1.2 s loop of 41 engine pulses with their harmonics, valve noise and a rumble, played at a pitch of 0.62 to 2.3 through four gears (revs climb in a gear and fall at the change, a cut of gas lets them
  drop, in the air they race), the hiss of the sand as wind rising with speed, and the crash. The volumes follow the *Effects* setting and the pause menu silences the engine (not in co-op, where the pause stops nothing).

**How it works** (`Deserto/DesertBike.cs`, `DesertBikeMesh.cs`, `DesertBikes.cs`, `SoundMoto.cs`, `PlayerController.Ride.cs`).

- **It is not a vehicle with wheel colliders: the rider is still the player's own `CharacterController`.** While riding, `PlayerController.UpdateBody` calls `RideTick` (`PlayerController.Ride.cs`) in place of the walking code, with the same
  inputs (keys, pad and touch alike, and the seat's own input in a split screen), and the player moves with `cc.Move` as always (the step offset is raised to 0.5 m for the ride). So the sand's colliders, the slopes, the falls, the damage, the Downed state, the
  hits of the enemies, the pose that co-op sends and the seats of the split screen keep working with nothing to change, and the bike is a body that follows the player (`DesertBike.Pose`). The price is that the handling is an *arcade* model, not
  a simulation, and that two bikes do not push each other (a parked bike has a collider and nothing walks through it; one with a rider has none, as the rider's body is on it).
- **The model of the ride**: heading and velocity are kept apart. The heading turns with the steering (a rate that falls from 2.3 rad/s at a crawl to 0.55 at top speed, 40 % more with the handbrake, and fades to nothing under 1.8 m/s: a bike does not turn on the spot); the velocity follows
  the heading with a lateral grip that damps the sideways part by 9 per second (1.2 with the handbrake, 0.15 in the air), so a bend taken fast slides; gas accelerates by `12 * throttle * (1 - v / top)`, the brake takes 17 m/s², the engine alone `1.4 + 0.006 v²`, the handbrake 20;
  gravity is the player's own (17.5) and a ground snap that grows with speed (`0.30 + 0.012 v` m) keeps the wheels on a downhill. The turbo's top speed eases in and out (5 and 9 m/s each second).
- **The sand is not outrun**: if the ground ahead of the bike has no collider yet (`DesertTerrain.Ready` on a point 0.9 s of travel plus 8 m ahead), the speed is brought down to 7 m/s (14 m/s²) until it has, so the bike never rides out onto sand that is not there (the 150 m round the player
  are always made with colliders, and at 31 m/s that is five seconds ahead).
- **The look is made by code** (`DesertBikeMesh.cs`, with the lathe of `DesertLathe`): frame, engine with its fins, exhaust, tank, seat, fork on the inclined steering axis, handlebar with grips, wheels with spokes, the lens of the headlight and the tail light, a
  number plate; the steering group is turned as a whole on the fork's axis; the rider's arms hold the bars and are seen only by the one who rides (and not in a split screen): leather gloves that turn with the fork and, for each side, a forearm (khaki canvas) that is a tube of length one that
  `DesertBikeMesh.Rig.PoseArms` stretches every frame between an elbow fixed in the body of the bike (below the lower edge of the screen) and the fist on the grip, so that the arm bends towards the grip when steering instead of turning with the fork. The pitch comes from two rays, one for each wheel,
  the roll from the steering and the speed, and the wheels turn with the distance. Everything is shared (one set of meshes and materials) among the bikes of a game; the paint is a dark red.
- **The rider seen from outside** (the body of `SurvivorBody`, driven by `SeatAvatar` in a split screen and by `RemotePlayer` in co-op) is seated: the pelvis on the saddle, the torso leaning 52° forward, the ankles on the footrests and both hands on the grips
  (`DesertBike.GripWorld`: the two-bone IK of the arms and legs, the same as on foot, aims at the grips and the footrests). The root of the body follows the body of the bike, so the rider leans into a bend with it; the weapon is hidden while seated, the crouch is
  ignored and the pose blends in and out in a fraction of a second (`SurvivorBody.SeatAmount`). The tests of the split screen and of co-op check it in numbers (seated, legs still, the two hands less than 12 cm from the grips in all: `RideHandsOff`).
- **Mounting** is a `Proc.IInteract` like the doors (radius 3.1 m; the bike has a box collider so that the sight line finds it). When a rider goes down or dies the bike is let go where it stands, and when that player is reborn at the stair the bike goes back to where it was born (`DesertBikes.ReturnHome`, called by `PlayerController.Respawn`); a bike that falls under the world goes back there too.

**Co-op and split screen** (protocol `pe-coop-32`: `PlayerNet` carries a bit `riding` in the second flags byte and a byte of `heading`, the direction the bike points, which is not where the player looks). A companion who rides is seen on a bike (`RemotePlayer.TickBike` makes
the puppet's bike when needed, poses it with the heading and the speed that the network gives, and puts the body in the saddle: seated, hands on the grips, as above), with the engine as a sound in space at the bike (not measured), and the way it leans comes from how fast it turns. Each machine builds the bike of its own slot
only, so **a companion's parked bike is not seen by the others** (only while he rides it); a companion who gets off in front of you leaves no bike in your world. In a split screen the seat's body is the rider (`SeatAvatar`: looking where the bike points, seated on it as above) and the bikes
are one for each seat. There is no collision between a bike and an enemy that hurts the enemy: **nothing runs over**, since it would need a damage source of its own over the network.

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
| `Deserto/SiteFacade.cs`, `Proc/FloorBuilder.Facade.cs`, `Proc/FacadeKit.cs` | The outside of the places (roofs, cornices, windows, ruins, the corner turrets of the towers) and their far skins. |
| `Deserto/Enemy.Desert.cs` | The enemy of the desert: rests, wakes, gives up. |
| `Deserto/DesertSky.cs`, `Resources/Shaders/DesertSand.shader`, `DesertSky.shader` | The sun and moon light, fog, exposure and the sky dome (stars, clouds, the sun's disc); the sand with its grain and ripples. |
| `Deserto/DesertGrade.cs` | The colour grade of the open desert by sky and weather (pure, also in the lab); `DesertSky` applies it and gives the room's look back indoors. |
| `Deserto/DesertNav.cs` | The sliding navmesh (0.9.1: in co-op the host also builds the tiles round every companion). |
| `Deserto/DesertMapGen.cs`, `DesertMapTiles.cs`, `HUD.DesertMap.cs`, `DesertHome.cs` | The minimap (the pure painter, also in the lab; the wrapping texture and its painting jobs; the HUD with the markers) and the way home (the golden arrow). |
| `Deserto/DesertAnchors.cs`, `Coop/CoopGame.World.cs`, `Coop/CoopTest.Desert.cs` | Co-op: the companions' points that the host builds the desert round (and, in a split screen, the other seats, which are also looked at); what each guest is sent (enemies near it, the clock, the boss); the two-process test. |
| `Deserto/DesertDirector.cs`, `DesertSites.cs`, `DesertStairs.cs` | The director (places, walkers in the open, loot in the open, bosses, the edge of the world, the renewal of the Library, the sweep of unused assets, the compass words), a loaded place and the stairs of a stack. |
| `Deserto/DesertPickup.cs` | Points, ammunition, medikit and essence. |
| `Deserto/DesertBike.cs`, `DesertBikeMesh.cs`, `DesertBikes.cs`, `SoundMoto.cs`, `PlayerController.Ride.cs` | The motorbike: the bike (mount, pose, engine, lights, parking), its model made by code, one bike for each seat, the engine and crash sounds (`SoundGen`, `SoundBank`), and the ride itself inside the player's body (the dynamics, the camera, getting off). |
| `Deserto/DesertCathedral.cs`, `Proc/RoomKits.HubDeserto.cs`, `Proc/HubPlan.cs` | The cathedral in the desert: plinth and stair (one stone mesh, physics in hidden boxes), the two braziers (a bowl, three legs and a ring in one mesh, `BrazierMesh`, built with `DesertLathe`) with their fire (`BrazierFire`), the two banners of the stair and those of the towers (`DesertBanner`), the lamp posts (`DesertLamps`) and the awnings, drapes and counters of the stalls (`DesertAwning`), the armoury and its prices, the places of the two works. |
| `Proc/FloorDirector.Desert.cs`, `FloorDirector.Catacombe.cs` | Starting the game, the wait for the ground, the HUD and the objective, the fall guard, what a rebuilt cathedral must not wipe. |
| `GameMenu.Mode.cs` | The choice *Standard* / *Il deserto*. |
| `DesertTest.cs`, `DesertTest.Moto.cs`, `SplitDesertTest.cs` | The in-game check (below), its `moto` phase and the one of the split screen. |

Changes elsewhere: `Stealth.Enabled` is off in the desert (no stealth: see `Enemy.Desert.cs`), `Enemy.FloorDirectorCeiling` and `MoveFlying` follow the ground, `WallBuy` has free
panels, `Catacombe.OnKill` takes the branch of a drop from the place, the standard gates of the descent are not built, `HUD` has a shade at the top
and outlined texts and a two-line objective while the desert is on, `ProcAmbience` and `PlayerController.Surface` give the wind, the dry echo and
the sand steps (`SoundGen.Step` surface 3), the menu's *Gioca* asks first (`GameMenu.Mode.cs`; `PadMenuTest` and `PresentationTest` choose
*Standard*), and `Shaders/ChurchHaze.shader` measures the height of its haze from the floor of the room instead of from the world's zero. For the motorbike `PlayerController.UpdateBody` hands over to `RideTick`
while `Riding` and lets go of the bike when the player goes down or dies, `Equipment`, `Knife` and `MagicCaster` do nothing while `Riding`, and `RemotePlayer`, `CoopGame.MyState` and `SeatAvatar` put the body in the saddle and send or follow the direction of the bike.

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
- **No sheet of the places seen, but a minimap** (0.9.1): the cathedral has never had a map (`M` does nothing there or in the desert); the objective names the nearest place and the way home, and
  since 0.9.1 the minimap (`N`) and the golden arrow do the same job better. A map of the places already seen is still the obvious next step if they are not enough.
- **Co-op shares the enemies, the hour and the fall of a boss, not the loot** (0.9.1): what lies on the sand and the trophies are each player's own, like the points and the store; the
  alternative (one list of what was taken for everybody) would have meant every pick-up crossing the wire and a player who walks ahead emptying the sand for the others.
- **190 m and 230 m**: the distances at which the host starts and stops sending an enemy to a guest (the sand is flat and the sight line long, so a monk at 190 m is already a threat; the 40 m
  between the two keep an enemy on the edge from appearing and disappearing). They are `InterestIn` and `InterestOut` in `CoopGame.World.cs`.
- **The more players, the more enemies on the sand**: +70 % of the wandering target and +12 of the cap for every companion, and the wandering enemies are born one player at a time. It is a guess.
- **Not saved**: the desert, its seed, the clock, what you took. The cathedral, the store and the works are saved as always.
- **The split screen shares the look and the ears** (0.9.1): the fog, the ambient and the colour grading are global in this pipeline, so a seat inside a building beside a seat on the sand gets the outdoor look (a little bright for the
  one indoors), and there is one audio listener, seat 1's, so a seat kilometres away from it hears nothing of what happens round it. A volume per camera would fix the first and a custom audio mix the second; neither was done.
- **The motorbike is an arcade ride on the player's body, not a vehicle** (19 November): the rider stays the `CharacterController` and everything that knows "the player" keeps working. A real vehicle (wheel colliders, a rigid body, suspension) would
  have meant a second body to keep in step with the first in co-op, the split screen, the falls and the enemies' hits. The numbers (83 km/h, 112 with the turbo, a stop from top speed in about 16 m) are a guess at what feels like a bike on sand, since nobody has ridden it by hand; they are constants of `DesertBike` and `PlayerController.Ride.cs`.
- **F both mounts and gets off** (it is the use key; the turbo is the sprint key and the handbrake the jump key, so a rider's hands never leave the keys they know). Getting off at speed is a brake first, not a jump.
- **One bike for each player, at the stair, not saved**: a bike left in the desert stays where it was left (it is let go where it stands when its rider goes down or dies), and the one at the stair is the same bike, except that **a player who dies and is
  reborn at the stair finds it back where it was born** (otherwise whoever died far away would have to walk back to it). A bike left on purpose is not called back: a button for that, or a second bike, is the obvious next step if walking back is a nuisance.
- **The bike is loud, and nothing runs over**: the engine's noise while it moves (7 m plus 0.85 m for every m/s, up to 26 m) is the price of the speed, and the bike is no weapon (no damage to an enemy it hits, which would have needed a damage source and a message of its own).
- **The first person only on the bike** (no third-person view while riding; the third-person view of 0.8.0 stays for walking), and no weapon: a rider with a gun on a fast bike would need a lot more (aiming from the saddle, the recoil on the handling, a hand taken off the bar).

## How to check

```powershell
dotnet run --project tools/desertlab -c Release            # the pure code, no Unity (a few seconds once built); add -- --quick for fewer samples
& $exe -batchmode -nographics -autotest -desertotest -seed 1000        # the real game, from the cathedral
& $exe -batchmode -autotest -desertotest -desertshots -seed 1000       # the same with a window and the photos of hotel_build/deserto
& $exe -batchmode -autotest -desertotest -desertonly varieta -desertshots -seed 1000   # one of the long phases (below): soak, varieta or prestazioni
.\tools\coop\run_all.ps1 -Only j                                       # the desert in co-op: a host and a guest, two processes, no graphics card (about three minutes)
.\tools\coop\run_all.ps1 -Only m                                       # the motorbike in co-op: one rides and the other watches, then the other way round (two processes, no graphics card)
& $exe -batchmode -autotest -desertotest -desertonly moto -desertshots -seed 1000   # the motorbike alone, with the ten photos (graphics card: not while another game runs)
```

Three more flags help when something fails: `-desertonly scalinata,cappella` runs only those phases (the arrival always runs; the others are
`cielo`, `cattedrale`, `opere`, `oggetti`, `suono`, `minimappa`, `torre`, `boss`, `catacomba`, `notte`, `morte`, `lontano`, `caduta`, `foto`), `-desertnavlog` writes, for
every navmesh tile, how many sources it collected and how many triangles the navmesh has when it is done, and `-desertdiag` (with `-desertshots`) says why
a hall looks wrong: ambient light, fog, the six nearest lights, the nearest wall and what it is made of, how many renderers are around, and three more
photos (towards the wall, at the floor). With a window the test also measures the contrast of what the camera sees in the chapel, in every level of the
catacomb and in the two boss halls (`FrameSpread`: a fog or a black room gives almost none).

Twenty-four phases are long (or are only photographs) and run only when asked by name (`-desertonly soak`, `varieta`, `prestazioni`, `costi`, `esterni`, `rovine`, `orizzonte`,
`paesaggio`, `nuvole`, `cose`, `galleria`, `rilievi`, `look`, `suolo`, `orme`, `creste`, `salina`, `cammino`, `percorso`, `vita`, `moto`, `motosuono`, `motourto` or `motocattedrale`, one or more per run, separated by commas), since the default run would double in time. Most are the ones of the livelier desert,
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
and checks that at least two crests start to smoke within 45 s, that there are live particles and that they are not too many, then shoots ahead, into the low sun and to the side; **`salina`** (three checks, eight photos with `-desertshots`) goes to the first salt flat, finds a flat spot in the middle of a lake and a dry shore with a lake ahead, checks that there are lakes and that a step in the water leaves no print while one on dry sand does, and shoots from the shore over the lake at dawn, noon, dusk and night, from the water towards the low sun at dawn and at dusk (the path of glints), at the stars and at the water's surface from 3.5 m; **`cammino`** (two checks, no photos) walks on foot from the foot of the stair to the landmark tower, 309 m of open sand, as a player does (the bot steps aside when
something blocks the way), and measures every frame: the median, the 95th and 99th percentile, the longest and how many pass 33, 50, 100 and 250 ms; it fails on a frame of half a second. Every other check
teleports or takes photos, and a photo costs 150 to 650 ms by itself, so none of them could see the hitches of a walk (a place that builds, a navmesh tile, a square of sand, the memory sweep). Its first run
said 150 ms twice, at 3 s and at 42 s: they were the test harness's own photos (`GameManager.HarnessShotFrame`: `auto_cam1`, `auto_cam2`, `auto_shot1` and `auto_shot2`, taken at fixed times), which the probe and the
frame log of the test now leave out. Without them, in the last run: 60,715 frames in 91 s, median 1.5 ms, 95 % 2.0, 99 % 2.6, the longest 23 ms and none over 33 (the run before: 62,477 frames, the longest 25 ms). The director also writes into the log every step of building a place that takes
30 ms or more, with its kind (shell, furnishing, loot, guard, removal) and its level: there is none in the walk, but the tests that teleport show the shells of level 0 at 30 to 36 ms and the one of the welcome chapel at 61 ms: a step is not split, so it can exceed the 7 ms that the loop of the director allows itself in a frame (a place that appears while you walk could show it as one slow frame: that is what `percorso` measures); **`percorso`** (three checks, no photos, headless) is the walk
that crosses the loading boundaries, which the one of 309 m does not (everything within reach is built on arrival): from the foot of the stair it picks, among 24 headings towards the south, the one that makes the most places load (they enter the radius of 280 m, or 340 for a tower, on the way and were not there at the start)
without touching any place (18 m), any mesa (40 m) or the cathedral, waits for the director to have built everything within reach, stops the clock at 9:00 and walks 1,300 m on foot, measuring every frame as `cammino` does and counting the slow steps of the director, the places loaded and unloaded
and the memory sweeps; for every frame over 33 ms it says whether a slow step or the unload of a place fell in it. It does not draw (with `-batchmode` the camera does not), so it measures the hitches of the logic and not the card; **`costi`** measures what each part of the picture costs in one place (see "The cost of a frame" below); **`esterni`** takes photos of the outside of one building of every kind and state, two towers, two catacombs and the chapel (four views from the
corners and one from above) and the cathedral from four sides; **`rovine`** (six checks, no photos by itself, headless, **needs no graphics card**) builds the shell of up to 33 places (28 buildings of every kind and state, three towers, two catacombs, the nearest to the cathedral first) twice, with the walls of 0.9.0 (`FloorBuilder.LegacyWalls`) and with the new ones, and counts on the real triangles (`DesertCensus`): the flipped faces, the faces of different materials that fight on the same plane and are seen from somewhere (the back faces that face into a wall are not counted, and the number is split into facade faces and the interiors, which are not touched), the 2,400 rays per place from the sand at eye height (a tenth from higher up, a quarter from farther) whose first surface is seen from behind, and the window panes seen from behind; it fails if there is any flipped face, if the facade fights are more than 5 % of those of 0.9.0, the holes more than 40 % and the panes more than 2 %, or if the same probe finds no defect in the walls of 0.9.0 (then it measures nothing). It takes about 50 seconds. `-rovinepng 6` also draws the first six places with the CPU (three views each, 640 x 360, into `hotel_build/deserto/census/`: magenta where the eye goes through a wall, yellow through a pane), `-rovinematch Chiesa/Intact|Casa/Ruin` the places whose kind/state contains one of the words, and `-rovineboth` the ones with the walls of 0.9.0 too, to put them side by side; **`orizzonte`** waits, from three points, for every far skin within 760 m to be built, checks that
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
and by night, the foot of the tower looking up, the nave, inside the chapel and the boss hall of a catacomb; a place over 33 ms fails. **`moto`** (42 checks, 52 with the arrival, the frame times and the log; headless in about two minutes,
with `-desertshots` ten photos `moto_a_fronte` to `moto_l_compagno_in_corsa` in `hotel_build/deserto/`) rides the whole ride: the bike parked at the foot of the stair (on the sand, with a collider, light off, silent, and a turquoise disc on the minimap where it stands), the use prompt and the mount (collider off,
weapon hidden, body on the bike, the disc out), 50 km/h in under 4 s, the heading, the top speed (21 to 24.5 m/s) and the turbo (over 27 and under 32.5), a bend (turning, leaning, the steered wheel), the brake (a stop in under 4 s), reverse, a stop by itself when the gas is let go, the handbrake slide
(the velocity leaves the heading by more than 6°), a wall at speed (the crash, the damage, no death at full health, the bike stopped), a lift into the void and the landing, the headlight (on at night, off by day), getting off (by itself when it stops, 0.7 to 2.4 m to the side in free
space, the collider and the weapon back, a walking body, the disc back), then **a ride of more than a kilometre**: three legs of 380 m (south, east, north) driven by an autopilot with three sphere casts to steer round obstacles, with the sand, the places and the navmesh made as it goes, which
checks that the rider arrives, never goes below the sand and has no frame over half a second; and last a companion's bike (a puppet posed by hand: no prompt, no collider, wheels turning, leaning in a bend). The `-coopmoto` scenario (`run_all.ps1 -Only m`) tests the network half with two processes (below).
**`motosuono`** (5 checks, 15 with the arrival, seconds) writes the three sounds of the bike (engine loop, crash, the sand's wind) as WAV files in `unity_logs/moto/`, to be measured and listened to outside the game, and checks them on their samples: that they
have some, that the engine neither clips nor carries a DC offset, that its loop closes without a jump at the seam, how much of its energy is above 80 and 150 Hz at its native pitch (what a small speaker or headphones can give back) and the same for the crash.
**`motourto`** (3 checks, 14 with the arrival, about 35 s) takes the same wall of the narthex six times at three speeds (the run-up decides it: 2.5, 5.9 and 13.85 m, that is 6.3, 9.6 and 13.6 m/s) and counts the crashes that the ride registers: none at 25 km/h (under 8 m/s a wall is only a wall) and all six
at 36 and at 49 km/h. It was written when the wall check of `moto` failed now and then: with the crash counted one frame at a time it gave **0, 3 and 6 of 6**; with the losses of a tenth of a second added up, **0, 6 and 6 of 6**.
**`motocattedrale`** (9 checks, 19 with the arrival, about 55 s with no graphics card; with `-desertshots` four photographs `moto_cat_*.png` from the saddle) freezes the clock at noon and rides the bike from where it stands up the stair, across the churchyard and through the portal into the narthex; it stops under the roof of the narthex, goes on through the arch into the nave as far as what shuts it, turns round, comes out and gets off on the churchyard. It checks the heights (the floor of the church), the passage at each stage, the headlight (on under the roof of the narthex at 12:00 with the sky's `Indoor` at 1.00, off in the nave, which has no roof), the dismount, and that no frame lasts half a second. It also writes what is in front of the bike where it stops: `Tavolato < Cantiere_volte < Arredi < 05_Navata`, 0.2 m away, at z 36.1: the building site of the Volte, which stands in the middle of the nave until the vaults are built.

## What is verified

Everything below was run on 18 and 19 November 2026 on the developer's PC (seed 1000); the numbers are those of the last run of each test, made on the working tree that was committed in the sixth round (`b9c6cd4`) or in the commit of the long-walk probe after it (`9eb7b60`), both on 19 November. The release battery of 19 November (everything but the photos and the cost of a frame) ran on the build of `9eb7b60`: `-desertotest` 127 of 127 in 171 s, `-desertonly soak,varieta,percorso` 48 of 48 in 733 s, `tools/desertlab` all pass.

- **The motorbike** (19 November 2026, on the code of this change, seed 1000, no one rode it by hand: the controls were forced by the test). `-desertonly moto`: **49 of 49** with the arrival, headless in 103 s and with a hidden window and the graphics card in 114 s (ten
  photographs, made again and looked at on the last build); the whole `-desertotest` after it **146 of 146** in 176 s (as before). What it measured: the bike stands 5.1 m from the foot of the stair, 0.00 m above the sand, with its collider, light off and silent; the use prompt picks it; in the saddle the body is on the bike
  (0.00 m), the collider is off and the weapon hidden; 50 km/h in 1.8 s; 21.0 m/s (76 km/h) at the time of the check against a top speed of 23, 27.0 then 29.2 m/s (105 km/h) with the turbo; a bend taken at speed turned the heading by 55° with a roll of 22° and the wheel
  steered by 28°; the brake took the speed of the test to 1.0 m/s in 1.3 s, reverse ran at -2.0 m/s, and without gas it stops by itself; with the handbrake the velocity left the heading by up to 90°; a wall taken at more than 50 km/h was a crash every time (the bike stopped, the camera was thrown) and cost 0 to 10 points of 150 from run to run (under 3 points nothing is taken; never a death); lifted 7 m into the void the wheels left the
  ground and it landed 0.08 m from the sand; the headlight is on at 23:00 and off at 12:00; getting off by itself when it stopped put the rider 1.05 m to the side in free space, the collider and the weapon came back; **the ride**: three legs of 380 m (south, east, north), 3 of 3 arrived in 49 s
  (about 1.1 km, 83 km/h on average), 340 to 360 blocks of sand and 11 places were made on the way, **0.0 m below the sand at worst**, the worst frame of the ride 147 ms in the headless run and 146 and 204 ms in the two runs with the graphics card (the check allows half a second; the longest frame of the headless run is 158 ms, two over 100 ms, in 97,428 frames); dying in the saddle 317 m from home threw the rider off with the bike left where it
  was (0.0 m moved) and, once reborn, the bike was back at its place (0.0 m). A companion's bike posed by hand: no prompt, no collider, wheels turning, rolling 11° in a bend. The first run found one failure, of the test (the roll of the companion's bike was waited for in 30 frames and without a window a frame lasts a
  millisecond: now a second and a bit of game time). **The photographs** (ten, looked at: the bike parked from four sides and from the stair, the saddle at a standstill, at 76 and 105 km/h, in a bend, at night with the headlight, and a companion's bike in the evening sun) found three real defects that no number had: the arms were black
  slabs and in a bend the shoulder turned with the fork and crossed the view (now forearms between a fixed elbow and the fist, with leather gloves), the flat shrouds beside the tank looked like panels (removed) and the headlight, set at 9 when the game's own torch has 118, hardly lit the sand (now 105: a clear pool
  on the sand ahead); a fourth came from the last pass: the sleeves, made lighter after the first, still looked black against the sand and are now khaki canvas (0.46 instead of 0.23). `run_all.ps1 -Only m` (two processes over loopback, no graphics card, 51 s): the guest rides while the host watches and then the other way round, **13 of 13 and 15 of 15**: the rider is seen riding, the bike appears under the puppet (0.00 m), runs in the direction it
  points (10 of 10 intervals of half a second, up to 75 km/h), leans 13.9° in a bend, its wheels turn (116° in 30 ms) and it disappears when the rider gets off. **Not verified**: nobody has ridden it by hand (the feel of the controls, of the camera, of the 83 and 112 km/h is a guess), the sound has not been heard
  (only its volume and pitch were measured), a real controller was not tried, the bike has not been ridden into the cathedral or into a place, nor with an enemy hunting the rider, and a companion's engine and parked bike were not checked in the game; and, in this first version, the rider seen from outside was the crouch pose put at the centre of the bike, a little lower and further forward than the saddle (replaced in the second round, below).
- **The motorbike, second round** (19 November 2026, after the pull request of the bike was open; seed 1000; still nobody rode it by hand, the controls were forced by the tests). Four changes, each measured:
  - **The rider seen from outside is seated** (see "How it works"). Measured: in the split screen the body of seat 2 is seated (pose 1.00, legs still) with its two hands **0.0 cm** from the grips, in the three sizes; in co-op the puppet's body is seated (pose 0.99) with its hands **2.0 cm** from the grips in all
    (both processes). Looked at: the two photographs of the split screen (seat 2 riding away from the stair and in a bend, seen by seat 1) show the pelvis on the saddle, the torso leaning forward, the arms to the bars and the whole body banking with the bike.
  - **The sounds were measured** (`-desertonly motosuono`, 5 checks; the WAV files in `unity_logs/moto/` were also analysed offline, with FFT and A-weighting). The first engine had 66 % of its energy under 80 Hz (42 % under 40) and 2.6 % of DC offset; a small speaker or headphones
    would have given back almost nothing at idle. Now (twenty harmonics, the body between 100 and 400 Hz, the offset removed): **78 % above 80 Hz and 43 % above 150 Hz** at the native pitch (the first version 34 % and 7 %), peak 0.75, no DC offset, and a loop whose seam jumps 0.021 where a normal step is 0.040. The first crash was 94 % one thump at 40 to 80 Hz;
    now **24 % of it is above 150 Hz** (2 % before). All of this is arithmetic on the samples: **nobody has heard it.**
  - **The minimap marks the parked bike** (a turquoise disc, `DesertBikes.Own`). Measured by the tests of `minimappa`, `moto` and the split screen: the disc is where the bike stands (12.6 m from the player at the top of the stair: 1.31, -4.54 px from the centre, as computed; 120 m to the side: 13.67, -42.87 px, as computed), goes out in the saddle and comes back
    when the rider gets off, is not drawn when the bike is still at its place and the golden arrow is on the same side, sits on the edge (88 px) on the bike's side when it is left 330 m away, and in the split screen each seat's disc is at its own bike (the bikes born 1.8 m apart are 0.7 px apart on the chart, the tolerance is 0.25). Looked at: the photograph `23_minimappa_moto.png`
    (the disc 45 px under and to the right of the red arrow, pale turquoise with a dark rim, clear of the golden arrow).
  - **A crash that was not always counted** (found by a check that failed in two of the three runs in which the `minimappa` phase ran before `moto`: *no crash* at 49 km/h, 150 points before and after, the sand ahead of the wall ready, so it was not the terrain governor). The crash was the speed lost in *one frame*, but the body touches a wall half-way through a frame and stops in the next.
    `-desertonly motourto` took the same wall six times at three speeds: before, **0, 3 and 6 of 6** crashes at 25, 36 and 49 km/h; after (the losses of braking harder than 60 m/s² added up over a tenth of a second), **0, 6 and 6 of 6**. The damage still depends a little on which frame is the first over 8 m/s (0 to 20 points at 49 km/h in the runs).
  Runs: `moto` with the `minimappa` phase **71 of 71** on the last build (70 of 70 twice on the one before, which lacked the last check of the minimap), `moto`, `motosuono` and `motourto` together **61 of 61** (on the one before); the whole `-desertotest` **149 of 149** in 179 s (before the crash fix and the last check of the minimap, which touch only riding and that phase);
  co-op `run_all.ps1 -Only m,j` **14 of 14 and 16 of 16** (m, 50 s) and **15 of 15 and 18 of 18** (j, 46 s); the split screen as above. **Not verified**, as before: nobody has ridden it by hand, the sound has not been heard, a real controller was not tried, the bike was not ridden into a place of the desert, nor with an enemy hunting the rider.
  - **The motorbike in the cathedral** (19 November 2026, same day, seed 1000, the controls forced by the test). `-desertonly motocattedrale`: **19 of 19** with the arrival, 54 s without a window. The bike, at 12:00, went up the stair (ending at (20.0, 0.1, 6.7): 0.1 m above the floor of the church), through the portal (3.2 m wide) to (18.0, 0.1, 19.8), braked to a stop within a metre under the roof of the narthex with the sky's `Indoor` at **1.00** and the headlight **on** at noon, rode through the arch (3.6 m) into the nave to z 36.1, where it stopped against the boards of the building site of the Volte, with the headlight **off** (the nave has no roof: `Indoor` 0.00), turned round in the width of the nave (12 m), came out through the portal to z 15.0 and the rider got off on the churchyard; the worst frame was 59 ms (157 ms in the whole run). The first run (15 of 18) found three mistakes of the test, not of the bike: a tolerance that stopped it at the portal, a target beyond the building site and a headlight check that asked for a roof where there is none. **Not looked at**: no photograph of the saddle in the nave was taken yet, and this checks the passage of the body, not how the portal looks.
- **The walls of the places (0.9.1)** (`-desertonly rovine`, 6 checks, 16 of 16 with the arrival, no graphics card, 192 s with three places drawn before and after; seed 1000, the 33 places nearest the cathedral: 28 buildings, 3 towers, 2 catacombs, 862,000 triangles): flipped faces **624 to 0**; faces of a facade that fight for the same pixels and are seen from somewhere **18,884 to 334** (of 20,472 to 1,922 in all: the rest are the interiors of the rooms, ceiling beams on the floor of the level above and crown mouldings flush with ceilings, the same before and after); rays that see through a wall **1,578 to 506** of 79,200 (none of the 506 is known to be a bug: they are rays that enter by a door, a broken window or a collapse; on the partitions of two ruined rooms there is a band along the top where the taller face is not seen from the lower side, which was not repaired); window panes seen from behind **949 to 3**. The images drawn by the CPU of a church, a ruined house and a collapsed inn were looked at before and after: the slits at the corners, the empty arcs under the vaults, the see-through windows and the see-through top of a ruin are gone from the walls of the church and the house; thin lines remain at the end of the vaults and along the partitions of the collapsed inn. **No photo with the graphics card**: the developer's game was open on the same PC (the CPU image is a flat-colour map, it does not judge the look).
- **Co-op in the desert (0.9.1)** (`tools/coop/run_all.ps1 -Only j,k,l`, 19 November 2026, protocol `pe-coop-31`, two processes over loopback on this PC, no graphics card, seed 2961, no exception in any log).
  `j_deserto` (46 s; guest 18 of 18, host 15 of 15): the desert is built on both machines (the guest in 2.9 s, the host in 9.9 s) and each sees the other; the guest has the host's seed and its hour of the day (7.25 h on both);
  put 499 m from the stair beside a ruined inn, the guest makes the host build that inn's shell, the sand with its collider and the navmesh under it (3.4 s); of two enemies born on the host, one beside each
  player, the guest gets the near one as a puppet and not the one 500 m away (the farthest puppet it sees is 89 m from it); when the guest comes back the two swap, and the one that went out of its sight is removed without dying (it is alive on the
  host); a lethal hit on a puppet kills the real enemy and pays the shooter (+100 points, +1 kill); the hour set on the host (day 2, 15.5 h) arrives (15.53 h); a boss that falls on the host (the host's `OnBossDown` is called
  directly: nobody fought it; a tower of 12 levels 1.5 km away) falls for the guest too and its trophy goes into both stores (39 to 60 resources). `k_deserto_esce` (41 s) and `l_deserto_ucciso` (57 s, the guest's process killed, exit -1 as expected; guest 10 of 10,
  host 16 of 16 in both): the guest, half a kilometre away, leaves; the host drops its puppet, has no anchor left to build round, has one target for the enemies, throws away the list of what that guest saw, unloads the far place
  (434 m from the host, threshold 370 m) and goes on alone (the clock runs 7.48 h to 7.60 h and an enemy born 14 m from it comes to 9.5 m in 6 s). The nine standard scenarios (a to i, 737 s) pass on the same protocol with the same
  numbers as on 17 November (see [../multiplayer/README.md](../multiplayer/README.md)).
- **Split screen in the desert (0.9.1)** (`-splitdesertotest` through `tools/coop/run_split.ps1 -Players N -Desert`, 19 November 2026, seed 1000): two seats 31 checks of 31, three 33 of 33, four 35 of 35, without a window (about 25 s each);
  two seats over **two real monitors** (the 3440x1440 primary and the 4K one) 33 of 33 in 30 s, 60 frames per second. The checks: every seat's camera has the far plane and the solid background; the sand, animals, veils and dust devils have one
  instance per seat and the fires one; the seats arrive 0.36 m apart; each HUD has the objective; seat 2, taken 2.6 km from seat 1 to a real chapel, stands on sand with a collider, with navmesh round it and still round seat 1, with props, loot
  and the chapel built next to it and its skin, and its sky is outdoors; it is not on seat 1's minimap (the seats that stayed home are); 70 points picked up by seat 2 are seat 2's and not seat 1's; the two objective lines differ (seat 2's says
  *cattedrale a 2550 m*); a seat sent past the edge of the world (18 km) is brought back to 17.9 km and the other does not move; brought back together, every minimap shows the others. With the graphics card each camera was rendered at noon (the top of
  the picture is sky, not the fog-coloured background: `split_posto1.png`, `split_posto2.png`) and the system photographed both monitors twice: the cathedral stair with the minimap, the objective and the companion at *2335 m* on one, a chapel
  2.3 km away with the golden arrow (*cattedrale · 2,3 km*) and the objective with the way home on the other; the sky of the two has the same colours from the top to the horizon (133,140,152 to 245,245,246 against 134,140,150 to 243,243,243);
  the dot of the other seat is on both minimaps when they are together. The motorbike (19 November 2026) added a step of 7 checks (one bike for each seat at its home; seat 2 gets on its own and rides it while the others stay on foot, with no arms
  of the bike on the screen and its body seen crouched on it; the bike stays parked where it gets off): two seats **38 of 38**, three **40 of 40** and four **42 of 42** (about 28 s each) without a window, and two seats with the graphics card and no window **40 of 40**, with the photograph that
  seat 1 takes of seat 2 riding away from the stair, its own bike parked beside it (`hotel_build/deserto/split_moto_posto2_in_sella.png`, looked at: the body of seat 2 is on the bike). The second round of the same day (a seated rider with its hands on the grips; the disc of each seat's bike on its minimap) made the step 10 checks:
  two seats **41 of 41**, three **43 of 43** and four **45 of 45** (28 to 30 s) without a window, and two seats with the graphics card and no window **43 of 43**, with two photographs of seat 2 on its bike, straight and in a bend, taken by seat 1 and looked at (seated, banking with the bike).
- **The pure code**: `tools/desertlab`, 107 checks, all pass (the list is in [../engineering/testing.md](../engineering/testing.md), section 2.4).
- **The real game**: `-desertotest`, 127 checks, all pass, in about three minutes without a window. In order: the game is in the desert and alone; the
  camera clears to a solid colour with a 1,500 m far plane (without it the scene's own sky is drawn over the dome); the cathedral is the usual plan with
  its armoury; the sand, the sky and the navmesh exist and the navmesh keeps every tile it has built; you start on the churchyard; no enemy in the
  cathedral; the stair has twelve steps of 20 cm, the sand at its foot is at the right height and does not cover the steps, you walk down it
  and the walkers get a navmesh on the sand but not on the stair or inside the cathedral; the two braziers at the head of the stair burn (four particle systems each) and stand on a tripod; the two banners hang from their bars and the wind moves their hem; an intact tower has a banner on a mast on its roof and one hanging on each long wall (four on the landmark), the wind moves them without ever pushing a wall banner into the wall, and a ruined tower has none, and the landmark tower has its corner turrets (its tallest facade rises 9.0 m over the roof, the battlements are 1.7);
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
- **With a window** (`-desertshots`, 46 checks of the halls: the chapel, the tower, the catacomb): the contrast of what the camera sees is 0.26 to 0.78 in
  the chapel (0.78), in the tower's boss hall (0.72), in the entrance hall of the catacomb (0.26, the darkest of all), in each of its three levels down to -14 m
  (0.38 to 0.58) and in its boss hall (0.53); a solid fog or a black room gives under 0.05. This check exists because of a bug that no other check saw: the haze of
  the church naves grew with the world height, so in the catacombs, below zero, every nave was a wall of grey and the boss hall a uniform blue picture; the
  photos were taken and the checks were green. Photos: 13 of the same run (arrival, dawn, noon, dusk, night, the chapel inside, the tower from far and from its
  foot, its first floor and its boss hall, a catacomb entrance and its boss hall), plus one per catacomb level and three of the tower's banners (from the entrance side, the cathedral side and the east), looked at one by one.
- **The long walk** (`-desertonly percorso`, 13 checks with the arrival, no window): 1,289 m in 332 s towards (1311, -146) with the seed 1000, 15 places loaded (as planned) and 21 or 22 unloaded (those of the start included: up to a tower of 496 objects), 5 memory sweeps of
  4 to 5 ms and, in the first and in the third run, about 410,000 frames with a median of 0.8 ms (headless: nothing is drawn) and a 99th percentile of 1.4 ms. One step of the director in the whole walk lasted over 30 ms (32 and 33 ms: the shell of level 0 of a complex) and no frame over 33 ms coincided with it or with the unload of a place.
  The first run had three frames over 33 ms: 52 ms at 6 m (2 s into the walk, when the first enemies of the places were being born; not looked into), 147 ms at 159 m (41 s, with the navmesh being built) and one under 50 ms. The second had the 147 ms frame again, at the same place and the same second
  (148 and 147 ms), which looked like something of the world and not of chance. The third, made on the final tree, had one frame over 33 ms (35 ms) and nothing at 159 m: so that frame does not come at every run, and its cause was not found (the unload of a place was ruled out by instrumenting it).
  The streaming of the places is smooth on this heading; an isolated frame of 50 to 150 ms in 332 seconds shows up in some runs and not in others.
- **The memory** (`-desertonly soak`, 9 checks, with a window). Three rounds of two far towers and two far catacombs, going home in between: live
  meshes 1,713, 1,718 and 1,719, materials 974, 1,012 and 1,012 (the caches of the facade palettes fill up in the first rounds and then stop), textures 419
  each time, memory 709, 753 and 763 MB (the test fails above 50 MB of growth between the last two rounds: 10). Before the sweep of unused assets was added the same test failed with 3,761, 5,545 and 7,317 meshes, 824, 1,328 and 1,688
  materials and 711, 860 and 983 MB: about 120 MB left behind by every four places. Four sweeps in the three rounds, the longest frame 1 ms and the last
  sweep 7 ms; after them the sand and the nave still have their contrast (0.52 and 0.51).
- **Every kind and state of place** (`-desertonly varieta -desertshots`, 40 checks). Eight buildings near the cathedral, seven kinds in four states
  (whole, ruined, split, collapsing; from a one-room house to a 24-room sanatorium): each is built, furnished, entered on foot from the sand through its door,
  walked through to the room that is farthest from the entrance (up to five doors away) and has a view with contrast (0.70 to 0.93 of the luminance range);
  a tower of 9 levels (entrance hall 0.47, boss hall 0.66) and a catacomb of 5 (entrance hall 0.40, boss hall 0.40): the shell of every level, the walk in from
  the sand, the boss in its hall. Three of the places of an earlier run (a ruined complex, a collapsed inn, a collapsed hotel) could not be entered at all:
  the doors of their crumbled walls were 1.5 m slots under a slab of ceiling; that is what `Opening.Gap` and the check `BUILDING_LOW_DOORWAYS_ARE_GAPS` of the
  lab are for. The photos of the theatre and of the tower's hall were looked at.
- **The cost of a frame** (`-desertonly prestazioni`, 15 checks) on an RTX 5060 Ti at 1920x1080, measured twice on the code of the sixth round (two runs about half an hour apart; the tumbleweed's
  surface changed in between, which this measure cannot see). First and second run: 8.2 and 6.7 ms on the churchyard facing the sand, 10.9 and 11.0 ms (day) and 9.6 and 8.8 ms (night) on the open sand with the
  tower in the distance, 6.4 and 13.0 ms at the foot of the tower looking up, 5.3 and 7.6 ms in the nave, 10.0 and 14.7 ms in the chapel, 8.4 and 9.0 ms in the boss hall of a catacomb, 6.0 to 7.2 and 5.5 to 9.6 ms on
  the open sand of each of the six regions, 7.5 and 7.0 ms at a night camp with its fire and 8.9 and 7.0 ms in a sandstorm. The worst place costs 10.9 and 14.7 ms: every place of both runs is under 16.7 ms
  (60 frames a second) and far from the 33 ms that the check allows. Two things are not explained. The open sand by day is 1.7 ms above the round before (9.3 ms) in both runs: the turrets of the far towers and the
  heavier tumbleweeds (about 4,000 triangles each, six or twelve on screen) are the only changes in that view, and neither looks big enough to cost that much, but no run isolated them. And the noise between two runs of
  the same code is much larger than the "about a millisecond" this page used to claim: the foot of the tower and the chapel differ by 6.6 and 4.7 ms, with nothing in those views that changed (the cause was not looked for: other programs of the PC use the card and the processor and the card
  changes its frequency, but none of that was checked). So the measure says "well below 33 ms" and cannot tell a change of two. The tripod braziers, the banners, the lamp posts and the awnings of the fifth
  round cost nothing that the measure could see (the worst place was 9.5 ms in the run before that and the difference was inside the noise). The heavier rocks and the new pebbles cost something, and it was measured: with the heavier rocks alone (every rock at all its triangles out to 280 m, every
  boulder out to 520 m, in every shadow map) the open sand was at 9.3 ms and the boss hall of a catacomb at 14.7 ms (11.1 ms before the rocks); the two versions of the rocks and the
  cut under the sand brought the catacomb to 7.7 ms and, in the run after the cacti and the bushes got their two versions as well, to 6.6 (the cut leaves almost nothing of the props to draw there, so the
  difference between those two is the noise of the measure: about a millisecond). `costi` (a measure, no check) takes each part away in turn, and the same part measured in two runs
  differs by about a millisecond, sometimes more. In the last run: at the churchyard (8.8 ms) the sun's shadows are 1.4 ms, the sand itself 1.1, the props on the sand 0.7, the life (animals and tumbleweeds) 0.7, the particles 0.6, and the point
  lights, the post-processing and the far places have nothing to show beyond the noise (the sand alone has measured 1.6, 2.5, 1.2, 1.9 and 1.1 ms in five runs with the same code: the claim that the sand's shader got heavier was not shown); on the open sand of the dunes (6.0 ms) the props are 1.4,
  the sand 1.9, the shadows 1.0 and the life 0.4; in the boss hall of a catacomb (9.2 ms) the point lights are 2.4, the shadows 1.1, the post-processing 1.1 and the props on the sand nothing (the cut under the sand); in the chapel (11.2 ms with 98 lights, and 10.0 and 14.7 ms in `prestazioni`: the measures of the same
  room disagree and why was not looked for) the point lights are 3.9, the props on the sand, that can be seen only through the windows, 2.1 and the sun's shadows 1.5.
- **Footprints, crests, salt flats** (`orme` 6 checks, `creste` 3, `salina` 5, with a window and photos): every step leaves a print (16 prints in 5.4 s over 21 m), a
  step in the air leaves none and the wind has erased them all after a minute and a half; at least two crests start to smoke and the particles alive are many but
  not too many (83); the salt flats have lakes, a step in the water leaves no print and one on dry sand does. The photos of the trail, of the crests and of the
  lakes at dawn, noon, dusk and night were looked at.
- **The rest of the game** (the standard mode shares files with the desert: the hub of the cathedral, the facade and the impact code), run again for the release on the build of commit `9eb7b60`, 19 November 2026, without a window except `-padtest`:
  `-cattedraletest` 72 of 72, `-chartest` 23 of 23, `-survivaltest` 91 of 91, `-impacttest` 7 of 7 (the sand is its eighth surface), `-architecturetest` 74 of 74, `-padtest` 89 of 89 (it goes through the mode panel), the split screen with two
  players `-seatstest` 89 of 89, three co-op scenarios (a, c, i: 62 and 49, 11 and 7, 66 and 53 checks) and `-maptest` 23 of 23. That last one has a check on time, the textures of the map on the main thread under 250 ms (238 ms in two runs): in the first run of the battery, made at low priority, they took
  252 ms and the test failed 22 of 23; the map code did not change, the wait is the main thread's for the worker that paints them (about 500 ms) at the start of a headless test, and the player builds the HUD long after the start. `MenuReview` (2874 of 2874) was last run on the tree of `dca7799` and not again: no file of the menu has
  changed since (a diff of the two commits says so), and the four rule labs of the CI gate and the Python tests were run by the gate (see [../engineering/testing.md](../engineering/testing.md), [display-and-menu.md](display-and-menu.md) and [controller.md](controller.md)).
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
- **The sixth round** (the same request, judged by photos): a tower was a box with battlements and, from the horizon, no outline; the cathedral, seen from the sand, a low enclosure with nothing above it. Both got
  corner turrets (a stone box, a cornice, a pyramid roof of slate, slit windows): the towers at the corners of their top floor, the cathedral at the four corners of the churchyard wall. By day the stalls of the market were black boxes
  under a coloured awning (the wood of the kit has an albedo of 0.1, made for dark corridors); they have a warm brown wood and a drape now. The tumbleweed was a ball of barbed wire; it took three versions (a dense one that the shader's contact shadow turned black, because the shadow is
  meant for things that stand on the ground and the tumbleweed rolls; then a brown boulder from 7 m; then the pale tangle of today), and two of the photos that judged them were of the wrong thing: the test aimed at a real
  tumbleweed that had already rolled away, so it photographed a rock. The test now puts one in front of the camera, at 7 m and at 2.6 m, and logs where it falls in the frame (the centre, 0.50 and 0.50). The probe `cammino` exists because
  no check could see the hitches of a walk, and its first run seemed to find two of 150 ms: they were the harness's own photos, and without them the longest frame of 309 m of walking is 23 to 25 ms. One battery of the round
  was interrupted: another game was started on the PC during its graphics step (a 26 s frame and regions two or three times slower in the measure, a failed check that was not the game's); the graphics steps were run again
  with the GPU free, and the runner now waits, and stops the run, while a Steam game is on.
- **Two checks that were fragile and were made robust**: the climb of the tower's stair failed once because a Monaco woken by the test stood on the first
  ramp (the test now removes the guards around the player while it walks, as the catacomb phase already did), and `creste` asked for two smoking crests in 25 s
  from a random search that gave 2 in one run and 1 in the next at the same wind (now eight times faster and 45 s).

What is **not** verified:

- Nobody has played it: the feel, the difficulty, the pace and the balance (prices, danger by distance, how much there is to find, the strength of the
  bosses, how long a day should be) are guesses. The sound (steps, wind, echo) was measured, never listened to.
- Only this PC was measured (one graphics card, a window in some runs and none in the others), and only with the seed 1000: the lab checks the generators
  on hundreds of seeds, but the game itself was run on one world. The soak test is three rounds of four places (minutes), not an hour of walking, and the
  far places (5 to 17 km) are reached by teleport, so the streaming along a walk is known for one heading and 1.3 km (`percorso`, with every frame measured, but without drawing: a real frame also pays for the card; `cammino` does 309 m). The cost of a frame is a
  manual render of the camera without the HUD, not a game played: real frames cost a little more.
- No physical controller in the desert. **The split screen in the desert** ran with real windows only for two seats (on two real monitors); three and four ran without a window. Nobody has played it with a second person: the sound of a seat
  kilometres from seat 1 (one listener), the cost of a frame with four seats looking at four different parts of the desert (not measured), a seat indoors beside one outdoors (the look is the outdoor one) and the night fires of two seats
  (`DesertFires.PickNear`, which fires to light for each seat, has no check of its own) were not looked at.
- **Co-op in the desert was tried with two processes on this PC over loopback and one guest**: never between two machines (latency, lost packets), never with three or four players in the desert
  (the floors were, with the same code), and the guest was never seen *moving*: the one photo of the pair (taken by the host with the graphics card, `-cooppics`) has the companion standing 23 m from it,
  with his body, his name and the light of his torch on the sand, the *Compagni* panel and his blue dot on the minimap. Nobody has seen a puppet appear at 190 m or a companion run across the sand.
  The distances of 190 and 230 m, the +70 % of enemies per companion and the way the host's frame grows with the players (it simulates and builds round every one of them) are guesses: the cost of a frame with
  four players was never measured. A guest who joins in the middle of a game was not tried in the desert (the code is the one of the floors and the guest's `FloorReady` hands it the enemies round it, which the
  scenario does exercise at the start: the guest joins 8 s after the host). A guest who leaves, closing the game or killed, was tried half a kilometre away (`k_deserto_esce`, `l_deserto_ucciso`); nothing was tried with a guest who dies in the desert.
- **The minimap and the arrow were seen in five screenshots of the game** (graphics card, 1600 x 900: `-desertonly minimappa -desertshots` takes `20_minimappa_scalinata`, `21_minimappa_lontano_notte` and `22_minimappa_lontano_giorno`, and the two co-op photos have it too), and they
  were judged by eye: the brass frame, the parchment with its contour lines and the footprints of the places, the red arrow that is you, the dot of an awake enemy (the one that sleeps is not there), the blue dot of the companion, the compass, the
  scale, the golden arrow on the edge pointing at the cathedral and, at the top of the screen, the arrow with *cattedrale · 550 m*. What the first photos found: the night chart was too dark (the inked footprints disappeared), and its tint was lightened (0.34, 0.40, 0.62 to
  0.46, 0.55, 0.85); it is still much darker than the day's. Not seen: the chart underground, in a storm, with four companions, at 4K or on an ultra-wide screen, or by someone who has never seen a map: whether it is easy to read at a glance is a judgement the developer has not made.
- Of the places in the open the test walks in and looks at one of each kind and state, a second tower and a second catacomb, but it climbs the stairs only
  of the landmark tower (to its first floor) and of the nearest catacomb (all the way down); the boss halls of the other two are reached by teleport, and the
  rooms between the entrance and the boss hall of the other towers have not been seen.
