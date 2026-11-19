# {{TITLE}}: 3D avatar Web App for Meta Ray-Ban Display

Exported from Unity by the Meta Display Toolkit (**Meta Display > 3D > Export Web App**).

The avatar is rendered **on the glasses** with Three.js, so it moves smoothly in real time. Nothing goes through the
Bluetooth link of the phone app. The optional relay lets the Unity app on the phone drive the avatar.

## Files

| File | What it is |
|---|---|
| `index.html` | Page shell: black background (black is transparent on the glasses), caption and Three.js import map |
| `app.js` | Scene, avatar (glTF model or the built-in robot), input, head orientation, voice tools, link with Unity |
| `config.js` | Settings: relay URL, room, model, actions, colors |
| `model.glb` | Your avatar, if one was exported (empty `modelUrl` = built-in robot) |
| `relay/` | Minimal WebSocket relay (Node.js) between Unity and the Web App |

## 1. Try it on the computer

Serve the folder over HTTP (modules do not load from `file://`):

```bash
npx serve .          # or: python -m http.server 8000
```

Open the page in Chrome with the **Meta Ray-Ban Display Simulator** extension (600x600 view, additive blending,
directional controls). Arrow keys and Enter behave like the glasses touchpad and the Neural Band.

## 2. Put it on the glasses

1. Publish the folder on any **HTTPS** host (GitHub Pages, Vercel, Netlify, Cloudflare Pages…).
2. In the Meta AI app: **App Settings > Apps > Web Apps > Connect Web App**, enter the HTTPS URL and save
   (Developer Mode must be on). The app appears at the bottom of the glasses app grid.

## Controls on the glasses

| Gesture | Effect |
|---|---|
| Left / Right | Rotate the avatar |
| Up / Down | Choose an action (shown at the top) |
| Select | Play the chosen action (and allow head tracking) |
| Turn the head | The avatar looks at you (`followHead`) |
| Voice ("Hey Meta, make the avatar wave") | Meta AI calls the `avatar_play_action` / `avatar_say` tools (WebMCP), when available |

## 3. Link with the Unity app (optional)

1. Deploy `relay/` on a Node.js host with TLS (Render, Railway, Fly.io…): `npm install && npm start`.
   For a quick test on your network, run it locally and expose it with a TLS tunnel (for example `ngrok http 8080`).
2. Put its `wss://` URL in `config.js` (`relayUrl`) **and** in the `GlassesWebAppLink` component in Unity,
   with the same `room` name.
3. In Unity, set the `Glasses3DView` strategy to **Web App**. `Play()`, `Say()`, `Rotate()` and `Look()` now
   drive this page; actions chosen on the glasses come back to Unity (`onAction`).

Messages are small JSON objects: `{ "type": "play", "name": "wave" }`, `say`, `rotate`, `look`, and back from
the glasses `action`, `input`, `hello`, `bye`. The relay forwards anything in a room: use an unguessable room
name and never send secrets through it.

## Your own avatar

Export it as **GLB with animations** (UnityGLTF in Unity, or Blender), save it as `model.glb` next to
`index.html` and set `modelUrl: 'model.glb'`. Animation names are matched to actions by name
(`wave` plays the first clip whose name contains "wave"). A bone whose name contains "head" follows the wearer's head.

Keep it light for the glasses: one skinned mesh, a few thousand triangles, small textures, and bright colors
(dark colors look transparent on the additive display).
