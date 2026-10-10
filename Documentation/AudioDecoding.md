# Embedded audio decoding

`load.payload.audioDecode` accepts `"native"` or `"software"`. An omitted/null value defaults to `"native"`; other values return `INVALID_AUDIO_DECODE` before replacing the active request. The existing `audioType` and transferable `audioBytes` fields remain required.

```js
iframe.contentWindow.postMessage({
  channel: 'ourtaiko-view', type: 'load', requestId: crypto.randomUUID(),
  payload: {
    chartText, audioBytes, audioType: 'ogg', audioDecode: 'software',
    course: 'Oni', practice: true, autoPlay: false, replay: false
  }
}, playerOrigin, [audioBytes]);
```

Native mode uses `AudioContext.decodeAudioData`. Software mode decodes **Ogg Vorbis and Ogg Opus** through bundled WASM libraries in a Worker; MP3/WAV continue through the browser decoder. The Ogg codec is determined from the identification packet, not the extension. Both modes produce AudioBuffers for the existing Web Audio clock, voices, volume, seeking and speed control. No Unity audio fallback or native OurTaikoPlay change is needed.

## Module boundaries

- Fanmade `src/embedded-player-audio.ts`: one fetch, retained Blob, new transferable ArrayBuffer per attempt, unique request IDs, timeout/cancellation and exactly one automatic native-to-software retry. `embedded-player.tsx` continues to validate iframe origin/source and display state.
- `WebPlayerBridge.Audio.cs`: applies the request's mode and waits for both the song and packaged sound effects before entering the scene. This detects unsupported Ogg effects even when the song is MP3/WAV. Missing assets are distinguished from decode failures.
- `WebAudio.Decoding.cs`: owns the clip decode cache; changing mode invalidates cached clips, and cancelling a request removes pending/failed clip decodes.
- `OurTaikoAudioDecode.jslib`: the C#/JS decode boundary and asynchronous buffer state. The existing `OurTaikoView.jslib` retains playback/voice management.
- Template `audio/decode.js`: native adapter, serial software work queue, PCM-to-AudioBuffer conversion and cancellation.
- Template `audio/ogg-worker.js`: lazy codec loading, whole-file decoding, error validation and transferable PCM. The Worker keeps compiled modules available; each file's decoder allocation is freed.
- Template `audio/vendor/`: pinned upstream bundles, source maps and licenses. No runtime requests to npm or third-party CDNs.

## Retry lifecycle

The parent starts in native mode and keeps its downloaded Blob until `loaded`, terminal failure, close or chart change. Only an `AUDIO_DECODE_FAILED` event with the exact current request ID triggers a software attempt. The parent immediately changes ID/mode, creates a fresh ArrayBuffer from the same Blob, then posts a second `load`. It does not download the song again. Duplicate/old events cannot start another retry; software failure is terminal. Each new chart load starts with native mode.

The player itself never silently retries. A loading event includes `stage: "audio"` and the selected `audioDecode`; a decode error's `detail` identifies `song` or `sound:<name>`. Closed requests discard late decode results. Software work is sequential to limit temporary memory, with a per-job timeout and cancellation that terminates the active Worker.

## Build and verification

Unity copies the template's audio directory into the player build. `publish-player.py` requires the adapter, Worker and both codec bundles and includes all files in the immutable version hash/upload/CDN validation. Deploy the new player before the new parent frontend; older players ignore the new mode and cannot perform software retries.

- Frontend: `pnpm test`, `pnpm lint`, `pnpm build`.
- Browser modules: in Fanmade/frontend, `pnpm exec playwright test e2e/embedded-player-audio.spec.ts`. `VIEW_WEB_ROOT` may override the sibling Web checkout. These tests use real bundled WASM/Workers and production jslib with a minimal scene host, not a Unity scene.
- Full player: rebuild WebGL and run `e2e/embedded-player-audio-unity.spec.ts` with `PLAYER_TEST_BUILD_DIR` pointing to the new build and `PLAYWRIGHT_BASE_URL` pointing to the frontend preview. This simulates native Ogg rejection in the real Unity player, checks one player audio fetch and two load modes, then completes an eight-note autoplay run. The chart page's separate HTML audio preview may also request metadata; that is not a fallback download. Keep the iframe visible so browser frame throttling does not suspend Unity's loading coroutine.
- Regression: `e2e/embedded-player.spec.ts` exercises the existing player behavior against the same build. Still validate both music and drum sounds, practice seek/speed, reload and exit on iOS 18.3.2 hardware.

Module tests alone do not establish full Unity scene acceptance, and desktop browser tests do not establish iPhone compatibility.
