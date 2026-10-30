// audio.js — effetti procedurali via WebAudio, zero asset.
export const AudioSys = {
  ctx: null, master: null, noiseBuf: null,
  init() {
    if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    this.ctx = new AC();
    this.master = this.ctx.createGain();
    this.master.gain.value = 0.5;
    this.master.connect(this.ctx.destination);
    const len = this.ctx.sampleRate * 1;
    this.noiseBuf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const d = this.noiseBuf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
  },
  env(gainNode, t0, peak, decay) {
    gainNode.gain.setValueAtTime(peak, t0);
    gainNode.gain.exponentialRampToValueAtTime(0.0001, t0 + decay);
  },
  burst({ dur = 0.15, freq = 1200, q = 0.8, peak = 0.5, type = 'lowpass', slideTo = 0 }) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const src = this.ctx.createBufferSource();
    src.buffer = this.noiseBuf; src.loop = true;
    const f = this.ctx.createBiquadFilter(); f.type = type; f.frequency.value = freq; f.Q.value = q;
    if (slideTo) f.frequency.exponentialRampToValueAtTime(Math.max(40, slideTo), t + dur);
    const g = this.ctx.createGain();
    this.env(g, t, peak, dur);
    src.connect(f); f.connect(g); g.connect(this.master);
    src.start(t); src.stop(t + dur + 0.05);
  },
  tone({ freq = 440, dur = 0.1, peak = 0.3, type = 'square', slideTo = 0 }) {
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    const o = this.ctx.createOscillator(); o.type = type; o.frequency.value = freq;
    if (slideTo) o.frequency.exponentialRampToValueAtTime(Math.max(20, slideTo), t + dur);
    const g = this.ctx.createGain();
    this.env(g, t, peak, dur);
    o.connect(g); g.connect(this.master);
    o.start(t); o.stop(t + dur + 0.05);
  },
  shoot() { this.burst({ dur: 0.13, freq: 3200, peak: 0.55, slideTo: 300 }); this.tone({ freq: 160, dur: 0.09, peak: 0.5, type: 'triangle', slideTo: 55 }); },
  dry() { this.tone({ freq: 1900, dur: 0.05, peak: 0.2, type: 'square' }); },
  reload() { this.tone({ freq: 500, dur: 0.06, peak: 0.25, type: 'square', slideTo: 300 }); setTimeout(() => this.tone({ freq: 900, dur: 0.07, peak: 0.25, type: 'square', slideTo: 500 }), 180); setTimeout(() => this.tone({ freq: 1300, dur: 0.08, peak: 0.3, type: 'square' }), 900); },
  hit() { this.tone({ freq: 2400, dur: 0.05, peak: 0.28, type: 'sine', slideTo: 1800 }); },
  kill() { this.tone({ freq: 700, dur: 0.16, peak: 0.35, type: 'sawtooth', slideTo: 140 }); },
  explosion() { this.burst({ dur: 0.7, freq: 900, peak: 0.8, slideTo: 60 }); this.tone({ freq: 90, dur: 0.5, peak: 0.6, type: 'sine', slideTo: 30 }); },
  step(run) { this.burst({ dur: 0.07, freq: run ? 700 : 500, peak: run ? 0.16 : 0.11, slideTo: 150 }); },
  jump() { this.burst({ dur: 0.1, freq: 600, peak: 0.12, slideTo: 1200 }); },
  land(hard) { this.burst({ dur: hard ? 0.2 : 0.1, freq: 350, peak: hard ? 0.35 : 0.18, slideTo: 90 }); },
  hurt() { this.tone({ freq: 220, dur: 0.2, peak: 0.4, type: 'sawtooth', slideTo: 90 }); },
  wave() { this.tone({ freq: 330, dur: 0.3, peak: 0.3, type: 'sawtooth', slideTo: 660 }); },
};
