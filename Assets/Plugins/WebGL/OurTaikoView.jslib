mergeInto(LibraryManager.library, {
  OurTaikoViewEmit: function(json) {
    if (window.ourTaikoViewEmit) window.ourTaikoViewEmit(UTF8ToString(json));
  },
  $OurTaikoDecoder: { generation: 0, state: 0, buffer: null },
  OurTaikoDecodeBegin__deps: ['$OurTaikoDecoder'],
  OurTaikoDecodeBegin: function(ptr, length, rate) {
    var ticket = ++OurTaikoDecoder.generation;
    OurTaikoDecoder.buffer = null;
    OurTaikoDecoder.state = 0;
    try {
      var context = new OfflineAudioContext(1, 1, rate > 0 ? rate : 48000);
      var bytes = HEAPU8.buffer.slice(ptr, ptr + length);
      context.decodeAudioData(bytes).then(function(buffer) {
        if (ticket !== OurTaikoDecoder.generation) return;
        OurTaikoDecoder.buffer = buffer;
        OurTaikoDecoder.state = 1;
      }).catch(function() {
        if (ticket === OurTaikoDecoder.generation) OurTaikoDecoder.state = -1;
      });
    } catch (error) { OurTaikoDecoder.state = -1; }
  },
  OurTaikoDecodeInfo__deps: ['$OurTaikoDecoder'],
  OurTaikoDecodeInfo: function(field) {
    if (field === 0) return OurTaikoDecoder.state;
    var b = OurTaikoDecoder.buffer;
    if (!b) return 0;
    return field === 1 ? b.length : field === 2 ? b.numberOfChannels : b.sampleRate;
  },
  OurTaikoDecodeCopy__deps: ['$OurTaikoDecoder'],
  OurTaikoDecodeCopy: function(ptr, count) {
    var b = OurTaikoDecoder.buffer;
    if (!b || count !== b.length * b.numberOfChannels) return 0;
    var base = ptr >> 2, channels = b.numberOfChannels;
    for (var c = 0; c < channels; c++) {
      var data = b.getChannelData(c);
      for (var i = 0; i < data.length; i++) HEAPF32[base + i * channels + c] = data[i];
    }
    return 1;
  },
  OurTaikoDecodeClear__deps: ['$OurTaikoDecoder'],
  OurTaikoDecodeClear: function() {
    OurTaikoDecoder.generation++;
    OurTaikoDecoder.buffer = null;
    OurTaikoDecoder.state = 0;
  }
});
