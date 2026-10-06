mergeInto(LibraryManager.library, {
  OurTaikoViewEmit: function(json) {
    if (window.ourTaikoViewEmit) window.ourTaikoViewEmit(UTF8ToString(json));
  },
  // Low-latency output: one AudioContext, shared decoded buffers and one restartable voice per sample.
  $OurTaikoAudio: { context: null, buffers: {}, voices: {}, next: 1 },
  $OurTaikoAudioLag__deps: ['$OurTaikoAudio'],
  // Seconds from currentTime (the scheduling horizon) to the speaker, measured where supported; diagnostics only.
  $OurTaikoAudioLag: function() {
    var ctx = OurTaikoAudio.context;
    var lag = (ctx.baseLatency || 0) + (ctx.outputLatency || 0);
    if (ctx.getOutputTimestamp) {
      var t = ctx.getOutputTimestamp();
      if (t && t.contextTime > 0 && t.performanceTime > 0) {
        var measured = ctx.currentTime - t.contextTime - (performance.now() - t.performanceTime) / 1000;
        if (measured > 0) lag = measured;
      }
    }
    return Math.min(Math.max(lag, 0), 0.25);
  },
  $OurTaikoAudioPeak: function(entry) {
    if (entry.peak === undefined) {
      var peak = 0, b = entry.buffer;
      for (var c = 0; c < b.numberOfChannels; c++) {
        var data = b.getChannelData(c);
        for (var i = 0; i < data.length; i++) { var v = data[i] < 0 ? -data[i] : data[i]; if (v > peak) peak = v; }
      }
      entry.peak = peak;
    }
    return entry.peak;
  },
  $OurTaikoAudioApplyGain__deps: ['$OurTaikoAudio', '$OurTaikoAudioPeak'],
  $OurTaikoAudioApplyGain: function(voice) {
    var entry = OurTaikoAudio.buffers[voice.buffer], gain = voice.volume;
    if (voice.normalize && entry && entry.state === 1) { var peak = OurTaikoAudioPeak(entry); if (peak > 0) gain /= peak; }
    voice.gain.gain.value = gain;
  },
  $OurTaikoAudioHalt: function(voice) {
    var source = voice.source;
    voice.source = null; voice.pending = null;
    if (source) { source.onended = null; try { source.stop(); } catch (error) {} source.disconnect(); }
  },
  $OurTaikoAudioStart__deps: ['$OurTaikoAudio', '$OurTaikoAudioHalt'],
  $OurTaikoAudioStart: function(voice, request) {
    var ctx = OurTaikoAudio.context, entry = OurTaikoAudio.buffers[voice.buffer];
    OurTaikoAudioHalt(voice);
    if (!entry || entry.state !== 1) { if (request.scheduled && entry && entry.state === 0) voice.pending = request; return; }
    var buffer = entry.buffer, rate = request.rate, offset = request.position, when = request.when, now = ctx.currentTime;
    // A late scheduled start keeps its timeline; immediate effects always play from their attack.
    if (when < now) { if (request.scheduled) offset += (now - when) * rate; when = now; }
    if (request.loop) offset %= buffer.duration;
    else if (offset >= buffer.duration) return;
    var source = ctx.createBufferSource();
    source.buffer = buffer; source.loop = request.loop; source.playbackRate.value = rate;
    source.connect(voice.gain);
    source.onended = function() { if (voice.source === source) { voice.source = null; source.disconnect(); } };
    source.start(when, Math.max(0, offset));
    voice.source = source; voice.started = when; voice.offset = offset; voice.rate = rate; voice.loop = request.loop;
  },
  OurTaikoAudioInit__deps: ['$OurTaikoAudio', '$OurTaikoAudioLag'],
  OurTaikoAudioInit: function() {
    if (OurTaikoAudio.context) return 1;
    var Context = window.AudioContext || window.webkitAudioContext;
    if (!Context) return 0;
    var ctx;
    try { ctx = new Context({ latencyHint: 'interactive' }); }
    catch (error) { try { ctx = new Context(); } catch (fallback) { return 0; } }
    OurTaikoAudio.context = ctx;
    var resume = function() { if (ctx.state !== 'running' && ctx.state !== 'closed') ctx.resume().catch(function() {}); };
    OurTaikoAudio.resume = resume;
    ['pointerdown', 'pointerup', 'touchend', 'mousedown', 'keydown'].forEach(function(type) { window.addEventListener(type, resume, true); });
    document.addEventListener('visibilitychange', function() { if (!document.hidden) resume(); });
    resume();
    return 1;
  },
  OurTaikoAudioInfo__deps: ['$OurTaikoAudio', '$OurTaikoAudioLag'],
  OurTaikoAudioInfo: function(field) {
    var ctx = OurTaikoAudio.context;
    if (!ctx) return 0;
    if (field === 0) return ctx.sampleRate;
    if (field === 1) return ctx.baseLatency || 0;
    if (field === 2) return ctx.outputLatency || 0;
    if (field === 3) return OurTaikoAudioLag();
    return ctx.state === 'running' ? 1 : 0;
  },
  OurTaikoAudioDecode__deps: ['$OurTaikoAudio', '$OurTaikoAudioApplyGain', '$OurTaikoAudioStart'],
  OurTaikoAudioDecode: function(ptr, length) {
    var A = OurTaikoAudio, id = A.next++, entry = { state: 0, buffer: null, refs: 1 };
    A.buffers[id] = entry;
    var done = function(buffer) {
      if (A.buffers[id] !== entry) return;
      entry.buffer = buffer; entry.state = 1;
      for (var key in A.voices) {
        var voice = A.voices[key];
        if (voice.buffer !== id) continue;
        OurTaikoAudioApplyGain(voice);
        if (voice.pending) OurTaikoAudioStart(voice, voice.pending);
      }
    };
    var failed = function() { if (A.buffers[id] === entry) entry.state = -1; };
    try {
      var bytes = HEAPU8.slice(ptr, ptr + length).buffer;
      var promise = A.context.decodeAudioData(bytes, done, failed);
      if (promise && promise.catch) promise.catch(failed);
    } catch (error) { entry.state = -1; }
    return id;
  },
  OurTaikoAudioBufferInfo__deps: ['$OurTaikoAudio'],
  OurTaikoAudioBufferInfo: function(id, field) {
    var entry = OurTaikoAudio.buffers[id];
    if (!entry) return field === 0 ? -1 : 0;
    return field === 0 ? entry.state : entry.state === 1 ? entry.buffer.duration : 0;
  },
  OurTaikoAudioRelease__deps: ['$OurTaikoAudio'],
  OurTaikoAudioRelease: function(id) {
    var entry = OurTaikoAudio.buffers[id];
    if (entry && --entry.refs <= 0) delete OurTaikoAudio.buffers[id];
  },
  OurTaikoVoiceCreate__deps: ['$OurTaikoAudio', '$OurTaikoAudioApplyGain'],
  OurTaikoVoiceCreate: function(buffer, normalize, speedChange) {
    var A = OurTaikoAudio, entry = A.buffers[buffer];
    if (!entry) return 0;
    entry.refs++;
    var gain = A.context.createGain();
    gain.connect(A.context.destination);
    var id = A.next++;
    A.voices[id] = { buffer: buffer, gain: gain, source: null, pending: null, volume: 1,
      normalize: !!normalize, speedChange: !!speedChange, started: 0, offset: 0, rate: 1, loop: false };
    OurTaikoAudioApplyGain(A.voices[id]);
    return id;
  },
  OurTaikoVoicePlay__deps: ['$OurTaikoAudio', '$OurTaikoAudioApplyGain', '$OurTaikoAudioStart'],
  OurTaikoVoicePlay: function(id, volume, loop, position, speed, delay, scheduled) {
    var A = OurTaikoAudio, voice = A.voices[id];
    if (!voice) return;
    if (A.resume) A.resume();
    voice.volume = Math.max(0, volume);
    OurTaikoAudioApplyGain(voice);
    // No output-latency compensation: music and hit sounds share the device latency.
    var ctx = A.context;
    OurTaikoAudioStart(voice, { loop: !!loop, position: Math.max(0, position), scheduled: !!scheduled,
      rate: voice.speedChange && speed > 0 ? speed : 1,
      when: ctx.currentTime + Math.max(0, delay) });
  },
  OurTaikoVoiceStop__deps: ['$OurTaikoAudio', '$OurTaikoAudioHalt'],
  OurTaikoVoiceStop: function(id) { var voice = OurTaikoAudio.voices[id]; if (voice) OurTaikoAudioHalt(voice); },
  OurTaikoVoiceSetVolume__deps: ['$OurTaikoAudio', '$OurTaikoAudioApplyGain'],
  OurTaikoVoiceSetVolume: function(id, volume) {
    var voice = OurTaikoAudio.voices[id];
    if (voice) { voice.volume = Math.max(0, volume); OurTaikoAudioApplyGain(voice); }
  },
  OurTaikoVoiceInfo__deps: ['$OurTaikoAudio'],
  OurTaikoVoiceInfo: function(id, field) {
    var A = OurTaikoAudio, voice = A.voices[id];
    if (!voice) return 0;
    var entry = A.buffers[voice.buffer], duration = entry && entry.state === 1 ? entry.buffer.duration : 0;
    if (field === 0) return voice.source || voice.pending ? 1 : 0;
    if (field === 2) return duration;
    if (field === 3) return voice.gain.gain.value;
    if (!voice.source) return voice.pending ? voice.pending.position : 0;
    var position = voice.offset + Math.max(0, A.context.currentTime - voice.started) * voice.rate;
    return voice.loop && duration > 0 ? position % duration : Math.min(position, duration);
  },
  OurTaikoVoiceFree__deps: ['$OurTaikoAudio', '$OurTaikoAudioHalt'],
  OurTaikoVoiceFree: function(id) {
    var A = OurTaikoAudio, voice = A.voices[id];
    if (!voice) return;
    OurTaikoAudioHalt(voice);
    voice.gain.disconnect();
    delete A.voices[id];
    var entry = A.buffers[voice.buffer];
    if (entry && --entry.refs <= 0) delete A.buffers[voice.buffer];
  }
});
