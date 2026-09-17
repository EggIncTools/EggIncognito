const SYMBOL = '__ZN19ParticleBatchedMesh11addParticleEN5Eigen9TransformIfLi3ELi2ELi0EEEf';
const MODULE = 'egginc';
const OUT_PATH = '/var/root/particle-capture.ndjson';
const DURATION_MS = 5000;
const MAX_RECORDS = 60000;

const ADDR_OFFSET = (typeof addrOffset !== 'undefined' && addrOffset) ? addrOffset : null;

function resolveAddParticle() {
    if (ADDR_OFFSET) {
        const m = Process.getModuleByName(MODULE);
        if (m) return m.base.add(ptr(ADDR_OFFSET));
    }

    let p = null;
    try {
        p = Module.findGlobalExportByName(SYMBOL);
    } catch (e) {
        console.warn('global export lookup failed: ' + e.message);
    }
    if (p) return p;
    for (const m of Process.enumerateModules()) {
        if (!/egg/i.test(m.name)) continue;
        let syms;
        try {
            syms = m.enumerateSymbols();
        } catch (e) {
            console.warn('symbol enumeration failed for ' + m.name + ': ' + e.message);
            continue;
        }
        for (const s of syms) {
            if (s.name === SYMBOL && s.address && !s.address.isNull()) return s.address;
        }
    }
    return null;
}

let warnedTransformRead = false;

function readTransform12(ptrTransform) {

    const out = new Array(12);
    try {
        for (let i = 0; i < 12; i++) out[i] = ptrTransform.add(i * 4).readFloat();
    } catch (e) {
        if (!warnedTransformRead) {
            warnedTransformRead = true;
            console.warn('transform read failed, particles will be dropped: ' + e.message);
        }
        return null;
    }
    return out;
}

const PROBE = (typeof probe !== 'undefined') ? probe : true;
const PROBE_HITS = 5;

function floatsAt(p, n) {
    if (!p || p.isNull()) return null;
    const out = [];
    try {
        for (let i = 0; i < n; i++) out.push(p.add(i * 4).readFloat());
    } catch (e) {
        console.warn('probe float read failed: ' + e.message);
        return null;
    }
    return out;
}

const target = resolveAddParticle();
if (!target) {
    send({ kind: 'error', msg: 'addParticle not resolved (set addrOffset)' });
} else {
    const out = new File(OUT_PATH, 'w');
    let count = 0;
    let stopped = false;
    send({ kind: 'ready', addr: target.toString(), probe: PROBE });

    const listener = Interceptor.attach(target, {
        onEnter(args) {
            if (stopped || count >= MAX_RECORDS) return;

            if (PROBE && count < PROBE_HITS) {
                const ctx = this.context;
                const rec = {
                    probe: count,
                    x0: args[0].toString(), x1: args[1].toString(),
                    x2: args[2].toString(), x3: args[3].toString(),
                    f_x0: floatsAt(args[0], 20),
                    f_x1: floatsAt(args[1], 16),
                    f_x2: floatsAt(args[2], 16),
                    s: [ctx.s0, ctx.s1, ctx.s2, ctx.s3].map(function (v) { return v === undefined ? null : v; }),
                };
                out.write(JSON.stringify(rec) + '\n');
                count++;
                return;
            }

            const meshPtr = args[0];
            const xform = readTransform12(args[1]);
            if (!xform) return;
            let size = 0;
            try {
                size = this.context.s0 !== undefined ? this.context.s0 : 0;
            } catch (e) {
                size = 0;
            }
            out.write(JSON.stringify({ t: count, mesh: meshPtr.toString(), x: xform, s: size }) + '\n');
            count++;
        },
    });

    setTimeout(function () {
        stopped = true;
        try {
            listener.detach();
        } catch (e) {
            console.warn('detach failed: ' + e.message);
        }
        try {
            out.flush();
            out.close();
        } catch (e) {
            console.warn('capture log may be truncated, flush/close failed: ' + e.message);
        }
        send({ kind: 'done', records: count, path: OUT_PATH });
    }, DURATION_MS);
}
