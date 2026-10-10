mergeInto(LibraryManager.library, {
  OurTaikoAudioSetDecodeMode__deps: ['$OurTaikoAudio'],
  OurTaikoAudioSetDecodeMode: function(software) { OurTaikoAudio.decodeMode = software ? 'software' : 'native'; },
  $OurTaikoAudioDecodeBytes__deps: ['$OurTaikoAudio', '$OurTaikoAudioApplyGain', '$OurTaikoAudioStart'],
  $OurTaikoAudioDecodeBytes: function(bytes) {
    var A = OurTaikoAudio, id = A.next++, entry = { state: 0, buffer: null, refs: 1 };
    A.buffers[id] = entry;
    var done = function(buffer) {
      if (A.buffers[id] !== entry) return;
      entry.cancel = null; entry.buffer = buffer; entry.state = 1;
      for (var key in A.voices) {
        var voice = A.voices[key];
        if (voice.buffer !== id) continue;
        OurTaikoAudioApplyGain(voice);
        if (voice.pending) OurTaikoAudioStart(voice, voice.pending);
      }
    };
    var failed = function() { if (A.buffers[id] === entry) { entry.cancel = null; entry.state = -1; } };
    try {
      var job = window.ourTaikoAudioDecoder.decode(A.context, bytes, A.decodeMode);
      entry.cancel = job.cancel;
      job.promise.then(done, failed);
    } catch (error) { failed(); }
    return id;
  },
  OurTaikoAudioDecode__deps: ['$OurTaikoAudioDecodeBytes'],
  OurTaikoAudioDecode: function(ptr, length) {
    return OurTaikoAudioDecodeBytes(HEAPU8.slice(ptr, ptr + length).buffer);
  },
  OurTaikoAudioDecodeTransferred__deps: ['$OurTaikoAudioDecodeBytes'],
  OurTaikoAudioDecodeTransferred: function(requestId) {
    var pending = window.ourTaikoPendingAudio;
    if (!pending || pending.requestId !== UTF8ToString(requestId)) return 0;
    window.ourTaikoPendingAudio = null;
    return OurTaikoAudioDecodeBytes(pending.bytes);
  }
});
