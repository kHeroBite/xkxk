# 제품 산출물(폴더·APK·웹 번들)의 xkxk.frameworks.ui* 어셈블리 간 TypeRef/MemberRef 폐포 정적 검사
# 사용: check_assembly_closure.py <폴더|apk> [...]   (라벨 붙이려면 <라벨> <경로> 쌍 반복)
#   메타데이터 루트(BSJB)를 직접 찾으므로 PE·webcil·XALZ 모두 처리한다.
# APK 레이아웃: Release=libassembly-store.so(net10=XALZ/LZ4, net11=XABA 구조+XAZS/Zstandard), Debug=lib_xkxk.frameworks.ui*.dll.so(ELF 포장 PE). 모두 없으면 rc=2.
# --self-test: 외부 파일 없이 스토어 파서·zstd 프레임(원시/RLE 블록)·전체 흐름을 합성 데이터로 검사한다(rc 0=통과).
# 종료 코드: 0=누락 없음, 1=Pd 타입/멤버 참조 누락, 2=검사 대상 없음·사용법 오류
import struct,sys,os,zipfile,hashlib,glob
def lz4(src,start,size):
    out=bytearray(); i=start
    while len(out)<size:
        t=src[i]; i+=1; ll=t>>4
        if ll==15:
            while True:
                x=src[i]; i+=1; ll+=x
                if x!=255: break
        out+=src[i:i+ll]; i+=ll
        if len(out)>=size: break
        off=src[i]|src[i+1]<<8; i+=2; ml=t&15
        if ml==15:
            while True:
                x=src[i]; i+=1; ml+=x
                if x!=255: break
        ml+=4; p=len(out)-off
        if off>=ml: out+=out[p:p+ml]
        else:
            for k in range(ml): out.append(out[p+k])
    return bytes(out)
# ---- net11 assembly store(XABA, Zstandard 'XAZS') 지원 ----------------------------------------------------------
# 형식 근거(dotnet/android main):
#   저장소 형식   https://github.com/dotnet/android/blob/main/Documentation/project-docs/AssemblyStores.md
#   압축 헤더     https://github.com/dotnet/android/blob/main/tests/Utilities/AssemblyStore/AssemblyCompression.cs
#   스토어 = ELF 공유 라이브러리의 'payload' 섹션. [헤더 20B: XABA, 버전, 항목수, 색인항목수, 색인바이트] [색인 9B*n] [기술자 28B*항목수] [이름: u32 길이+UTF-8] [데이터]
#   기술자 7*u32 = 매핑색인, 데이터오프셋(페이로드 시작 기준), 데이터크기, PDB오프셋·크기, config오프셋·크기
#   압축 데이터 = 'XAZS' u32, 기술자색인 u32, 비압축크기 u32, 그 뒤 Zstandard 프레임 (옛 net10 은 'XALZ' + LZ4, 위 lz4() 가 처리)
# Python 표준 라이브러리에 zstd 가 없어(3.14 부터 compression.zstd) zstandard 모듈이 있으면 쓰고, 없으면 아래 순수 Python 복호기로 푼다.
_LL_BASE = list(range(16)) + [16, 18, 20, 22, 24, 28, 32, 40, 48, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536]
_LL_BITS = [0] * 16 + [1, 1, 1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]
_ML_BASE = list(range(3, 35)) + [35, 37, 39, 41, 43, 47, 51, 59, 67, 83, 99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539]
_ML_BITS = [0] * 32 + [1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]
_LL_DEF = ([4, 3] + [2] * 11 + [1, 1, 1] + [2] * 9 + [3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1], 6)
_ML_DEF = ([1, 4, 3] + [2] * 6 + [1] * 37 + [-1] * 7, 6)
_OF_DEF = ([1] * 6 + [2] * 3 + [1] * 15 + [-1] * 5, 5)
def _hb(x): return x.bit_length() - 1
def _fse_read(d, o, maxsym):
    # FSE 정규화 카운터 헤더 읽기 -> (확률목록, 정확도, 다음 오프셋)
    bits = int.from_bytes(d[o:o + 400], 'little'); bc = 0
    def rd(n):
        nonlocal bc
        v = (bits >> bc) & ((1 << n) - 1); bc += n; return v
    al = rd(4) + 5; remaining = (1 << al) + 1; thr = 1 << al; nb = al + 1; probs = []; prev0 = False
    while remaining > 1 and len(probs) <= maxsym:
        if prev0:
            while True:
                r = rd(2); probs += [0] * r
                if r != 3: break
                # 3 이면 이어서 2비트 반복
        # prev0 처리: 위 루프는 (0 반복 개수) 2비트 단위 누적
        mx = (2 * thr - 1) - remaining
        low = (bits >> bc) & (thr - 1)
        if low < mx: cnt = low; bc += nb - 1
        else:
            cnt = (bits >> bc) & (2 * thr - 1)
            if cnt >= thr: cnt -= mx
            bc += nb
        cnt -= 1; remaining -= abs(cnt); probs.append(cnt); prev0 = (cnt == 0)
        while remaining < thr: nb -= 1; thr >>= 1
    return probs, al, o + ((bc + 7) >> 3)
def _fse_build(probs, al):
    size = 1 << al; sym = [0] * size; hi = size - 1; nxt = {}
    for i, p in enumerate(probs):
        if p == -1: sym[hi] = i; hi -= 1; nxt[i] = 1
        elif p > 0: nxt[i] = p
    step = (size >> 1) + (size >> 3) + 3; mask = size - 1; pos = 0
    for i, p in enumerate(probs):
        for _ in range(p if p > 0 else 0):
            sym[pos] = i; pos = (pos + step) & mask
            while pos > hi: pos = (pos + step) & mask
    nbs = [0] * size; base = [0] * size
    for st in range(size):
        x = nxt[sym[st]]; nxt[sym[st]] = x + 1; n = al - _hb(x); nbs[st] = n; base[st] = (x << n) - size
    return sym, nbs, base, al
class _Back:
    # 뒤에서 앞으로 읽는 비트 스트림(마지막 바이트의 최상위 1 비트가 끝 표지)
    def __init__(s, d):
        s.d = d; last = d[-1]
        if last == 0: raise ValueError('zstd: 끝 표지 없음')
        s.pos = (len(d) - 1) * 8 + _hb(last)
    def read(s, n):
        if n == 0: return 0
        lo = s.pos - n; s.pos = lo
        if lo >= 0:
            return (int.from_bytes(s.d[lo >> 3:((lo + n - 1) >> 3) + 1], 'little') >> (lo & 7)) & ((1 << n) - 1)
        have = lo + n  # 남은(유효) 비트 수, 음수면 0
        v = (int.from_bytes(s.d[0:(have + 7) >> 3], 'little') & ((1 << have) - 1)) if have > 0 else 0
        return (v << (-lo)) & ((1 << n) - 1)
def _huf_weights(d, o):
    hb = d[o]
    if hb >= 128:
        n = hb - 127; w = []
        for b in d[o + 1:o + 1 + (n + 1) // 2]: w += [b >> 4, b & 15]
        return w[:n], o + 1 + (n + 1) // 2
    probs, al, no = _fse_read(d[o + 1:o + 1 + hb], 0, 255)
    sy, nbs, base, al = _fse_build(probs, al)
    br = _Back(d[o + 1 + no:o + 1 + hb]); s1 = br.read(al); s2 = br.read(al); w = []
    while True:
        w.append(sy[s1]); s1 = base[s1] + br.read(nbs[s1])
        if br.pos < 0: w.append(sy[s2]); break
        w.append(sy[s2]); s2 = base[s2] + br.read(nbs[s2])
        if br.pos < 0: w.append(sy[s1]); break
    return w, o + 1 + hb
def _huf_table(w):
    tot = sum(1 << (x - 1) for x in w if x)
    mb = _hb(tot) + 1; last = (1 << mb) - tot
    if last & (last - 1): raise ValueError('zstd: Huffman 가중치 오류')
    w = w + [_hb(last) + 1]; ts = 1 << mb; sy = [0] * ts; nb = [0] * ts; p = 0
    for wt in range(1, mb + 1):
        for i, x in enumerate(w):
            if x == wt:
                c = 1 << (wt - 1); sy[p:p + c] = [i] * c; nb[p:p + c] = [mb + 1 - wt] * c; p += c
    return sy, nb, mb
def _huf_dec(d, tab, cnt):
    sy, nb, mb = tab; br = _Back(d); out = bytearray(); pos = br.pos; mask = (1 << mb) - 1; fb = int.from_bytes
    for _ in range(cnt):
        lo = pos - mb
        if lo >= 0: i = (fb(d[lo >> 3:((pos - 1) >> 3) + 1], 'little') >> (lo & 7)) & mask
        else: br.pos = pos; i = br.read(mb)
        out.append(sy[i]); pos -= nb[i]
    return bytes(out)
def zstd_decompress(d, o=0):
    # 프레임 1개를 푼다. 필요한 부분만 구현(사전 미지원) — 실제 net11 어셈블리 스토어 데이터로 검증.
    if d[o:o + 4] != b'\x28\xb5\x2f\xfd': raise ValueError('zstd: 매직 불일치')
    fd = d[o + 4]; o += 5; single = fd >> 5 & 1
    if not single: o += 1
    o += (0, 1, 2, 4)[fd & 3]
    o += (1 if single else 0, 2, 4, 8)[fd >> 6]
    out = bytearray(); reps = [1, 4, 8]; huf = None; tabs = [None, None, None]
    while True:
        h = d[o] | d[o + 1] << 8 | d[o + 2] << 16; o += 3; last = h & 1; ty = h >> 1 & 3; sz = h >> 3
        if ty == 0: out += d[o:o + sz]; o += sz
        elif ty == 1: out += d[o:o + 1] * sz; o += 1
        elif ty == 2:
            e = o + sz; b0 = d[o]; lt = b0 & 3; sf = b0 >> 2 & 3
            if lt < 2:
                if sf in (0, 2): rs = b0 >> 3; o += 1
                elif sf == 1: rs = (b0 >> 4) | d[o + 1] << 4; o += 2
                else: rs = (b0 >> 4) | d[o + 1] << 4 | d[o + 2] << 12; o += 3
                if lt == 0: lit = d[o:o + rs]; o += rs
                else: lit = d[o:o + 1] * rs; o += 1
            else:
                hv = int.from_bytes(d[o:o + 5], 'little')
                if sf == 0: ns = 1; nbit = 10; hl = 3
                elif sf == 1: ns = 4; nbit = 10; hl = 3
                elif sf == 2: ns = 4; nbit = 14; hl = 4
                else: ns = 4; nbit = 18; hl = 5
                rs = hv >> 4 & ((1 << nbit) - 1); cs = hv >> (4 + nbit) & ((1 << nbit) - 1); o += hl; le = o + cs
                if lt == 2:
                    w, o = _huf_weights(d, o); huf = _huf_table(w)
                elif huf is None: raise ValueError('zstd: Huffman 표 없음')
                if ns == 1: lit = _huf_dec(d[o:le], huf, rs)
                else:
                    s1, s2, s3 = (d[o + 2 * i] | d[o + 2 * i + 1] << 8 for i in range(3)); o += 6
                    q = (rs + 3) // 4; a = o; bnd = [a, a + s1, a + s1 + s2, a + s1 + s2 + s3, le]
                    lit = b''.join(_huf_dec(d[bnd[i]:bnd[i + 1]], huf, q if i < 3 else rs - 3 * q) for i in range(4))
                o = le
            ns_ = d[o]; o += 1
            if ns_ == 0: nseq = 0
            elif ns_ < 128: nseq = ns_
            elif ns_ < 255: nseq = ((ns_ - 128) << 8) + d[o]; o += 1
            else: nseq = d[o] + (d[o + 1] << 8) + 0x7F00; o += 2
            lp = 0
            if nseq:
                modes = d[o]; o += 1; defs = (_LL_DEF, _OF_DEF, _ML_DEF); mx = (35, 31, 52); tt = []
                for k in range(3):
                    m = modes >> (6 - 2 * k) & 3
                    if m == 0: tt.append(_fse_build(*defs[k]))
                    elif m == 1: tt.append(([d[o]], [0], [0], 0)); o += 1
                    elif m == 2:
                        pr, al, o = _fse_read(d, o, mx[k]); tt.append(_fse_build(pr, al))
                    else:
                        if tabs[k] is None: raise ValueError('zstd: 반복 표 없음')
                        tt.append(tabs[k])
                tabs = tt; (lsy, lnb, lba, lal), (osy, onb, oba, oal), (msy, mnb, mba, mal) = tt
                br = _Back(d[o:e]); sl = br.read(lal); so = br.read(oal); sm = br.read(mal)
                for i in range(nseq):
                    oc = osy[so]; mc = msy[sm]; lc = lsy[sl]
                    ov = (1 << oc) + br.read(oc); ml = _ML_BASE[mc] + br.read(_ML_BITS[mc]); ll = _LL_BASE[lc] + br.read(_LL_BITS[lc])
                    if i < nseq - 1:
                        sl = lba[sl] + br.read(lnb[sl]); sm = mba[sm] + br.read(mnb[sm]); so = oba[so] + br.read(onb[so])
                    if ov > 3: off = ov - 3; reps = [off, reps[0], reps[1]]
                    else:
                        ix = ov - 1 + (1 if ll == 0 else 0)
                        if ix == 0: off = reps[0]
                        else:
                            off = reps[ix] if ix < 3 else reps[0] - 1
                            if ix == 1: reps = [reps[1], reps[0], reps[2]]
                            elif ix == 2: reps = [reps[2], reps[0], reps[1]]
                            else: reps = [off, reps[0], reps[1]]
                    out += lit[lp:lp + ll]; lp += ll; p = len(out) - off
                    if off <= 0 or p < 0: raise ValueError('zstd: 오프셋 오류')
                    if off >= ml: out += out[p:p + ml]
                    else:
                        for k in range(ml): out.append(out[p + k])
            out += lit[lp:]; o = e
        else: raise ValueError('zstd: 예약 블록')
        if last: break
    return bytes(out)
def zdec(b):
    try:
        import zstandard
        return zstandard.ZstdDecompressor().decompress(b, max_output_size=1 << 29)
    except ImportError: pass
    try:
        from compression import zstd as _z  # Python 3.14+
        return _z.decompress(b)
    except ImportError: pass
    return zstd_decompress(b)
def elf_payload(s):
    # ELF 섹션 'payload' 의 (오프셋, 크기). 없으면 첫 XABA 매직 위치
    try:
        sh = struct.unpack_from('<Q', s, 0x28)[0]; es, n, si = struct.unpack_from('<HHH', s, 0x3A)
        secs = [struct.unpack_from('<IIQQQQ', s, sh + i * es) for i in range(n)]; so = secs[si][4]
        for nm, ty, fl, ad, off, sz in secs:
            if s[so + nm:s.index(b'\0', so + nm)] == b'payload': return off, sz
    except Exception: pass
    i = s.find(b'XABA')
    return (i, len(s) - i) if i >= 0 else None
def store_entries(s, skip=lambda n: False, want=lambda n: True):
    # XABA 스토어(net11) -> {모듈명: (바이트, 메타)}. want(이름) 인 항목만 복호한다(비 Pd 어셈블리는 건너뛰어 시간 절약).
    pl = elf_payload(s)
    if not pl: return {}
    p, size = pl; mg, ver, n, ie, isz = struct.unpack_from('<5I', s, p)
    if mg != 0x41424158 or isz != ie * 9: return {}
    d0 = p + 20 + isz; no = d0 + 28 * n; D = {}
    for i in range(n):
        ln = struct.unpack_from('<I', s, no)[0]; nm = s[no + 4:no + 4 + ln].decode(); no += 4 + ln
        ds, dsz = struct.unpack_from('<II', s, d0 + 28 * i + 4)
        if not dsz or not want(nm): continue
        raw = s[p + ds:p + ds + dsz]
        if raw[:4] == b'XAZS':
            n_un = struct.unpack_from('<I', raw, 8)[0]; raw = zdec(raw[12:])
            if len(raw) != n_un: raise ValueError('zstd: 비압축 크기 불일치 %s %d != %d' % (nm, len(raw), n_un))
        elif raw[:4] == b'XALZ': raw = lz4(raw, 12, struct.unpack_from('<I', raw, 8)[0])
        m = md(raw)
        if m: D[m['mod']] = (raw, m)
    return D
def md(b):
    r=b.find(b'BSJB')
    if r<0: return None
    vl=struct.unpack_from('<I',b,r+12)[0]; p=r+16+vl+2
    n=struct.unpack_from('<H',b,p)[0]; p+=2; st={}
    for i in range(n):
        o,s=struct.unpack_from('<II',b,p); p+=8; e=b.index(b'\0',p); name=b[p:e].decode(); p+=((len(name)+4)&~3); st[name]=r+o
    t=st.get('#~') or st['#-']; hs=b[t+6]; valid=struct.unpack_from('<Q',b,t+8)[0]; p=t+24; R={}
    for i in range(64):
        if valid>>i&1: R[i]=struct.unpack_from('<I',b,p)[0]; p+=4
    si=4 if hs&1 else 2; gi=4 if hs&2 else 2; bi=4 if hs&4 else 2
    u=lambda sz,q: struct.unpack_from('<I' if sz==4 else '<H',b,q)[0]
    S=lambda o: b[st['#Strings']+o:b.index(b'\0',st['#Strings']+o)].decode()
    g=lambda bits,*tb: 4 if max(R.get(x,0) for x in tb)>=(1<<(16-bits)) else 2
    rsz=g(2,0,26,35,1); tdr=g(2,2,1,27); mrp=g(3,2,1,26,6,27)
    fi=4 if R.get(4,0)>=65536 else 2; mi=4 if R.get(6,0)>=65536 else 2; pi=4 if R.get(8,0)>=65536 else 2; ti=4 if R.get(2,0)>=65536 else 2
    mvid=b[st['#GUID']+(u(gi,p+2+si)-1)*16:st['#GUID']+u(gi,p+2+si)*16].hex()
    mod=S(u(si,p+2)); p+=R[0]*(2+si+3*gi)
    tr=[]
    for i in range(R.get(1,0)):
        sc=u(rsz,p); nm=S(u(si,p+rsz)); ns=S(u(si,p+rsz+si)); p+=rsz+2*si; tr.append((sc&3,sc>>2,ns,nm))
    td=[]
    for i in range(R.get(2,0)):
        nm=S(u(si,p+4)); ns=S(u(si,p+4+si)); q=p+4+2*si+tdr; fl=u(fi,q); ml=u(mi,q+fi); p=q+fi+mi; td.append([ns,nm,fl,ml])
    assert 3 not in R and 5 not in R and 7 not in R,'ptr tables'
    fields=[]
    for i in range(R.get(4,0)):
        fields.append(S(u(si,p+2))); p+=2+si+bi
    meths=[]
    for i in range(R.get(6,0)):
        meths.append(S(u(si,p+8))); p+=8+si+bi+pi
    p+=R.get(8,0)*(4+si)
    p+=R.get(9,0)*(ti+tdr)
    mr=[]
    for i in range(R.get(10,0)):
        c=u(mrp,p); nm=S(u(si,p+mrp)); p+=mrp+si+bi; mr.append((c&7,c>>3,nm))
    members={}
    for i,(ns,nm,fl,ml) in enumerate(td):
        fe=td[i+1][2] if i+1<len(td) else len(fields)+1; me=td[i+1][3] if i+1<len(td) else len(meths)+1
        members[(ns,nm)]=set(fields[fl-1:fe-1])|set(meths[ml-1:me-1])
    return dict(mod=mod,mvid=mvid,tr=tr,td=td,mr=mr,members=members,typedefs=len(td),methods=R.get(6,0))
def fromapk(path):
    out={}; z=zipfile.ZipFile(path)
    for abi in sorted({n.split('/')[1] for n in z.namelist() if n.startswith('lib/') and n.endswith('libassembly-store.so')}):
        s=z.read(f'lib/{abi}/libassembly-store.so'); i=0; D={}
        while True:
            i=s.find(b'XALZ',i)
            if i<0: break
            sz=struct.unpack_from('<I',s,i+8)[0]
            try:
                d=lz4(s,i+12,sz); m=md(d)
                if m: D[m['mod']]=(d,m)
            except Exception as e: pass
            i+=12
        if not D: # net11 XABA 스토어(XAZS=Zstandard). xkxk.frameworks.ui* 만 복호하고 .r2r.dll(합성 네이티브 이미지, 메타데이터 없음)은 건너뛴다
            D = store_entries(s, want=lambda n: n.startswith('xkxk.frameworks.ui') and n.endswith('.dll') and not n.endswith('.r2r.dll') and '/' not in n)
        if not D: # 비압축 스토어
            i=0
            while True:
                i=s.find(b'BSJB',i)
                if i<0: break
                m=md(s[i:i+60000000])
                if m: D[m['mod']]=(None,m)
                i+=4
        out[abi]=D
    # Debug 레이아웃: 어셈블리 스토어 없이 lib/<abi>/lib_xkxk.frameworks.ui*.dll.so 개별 파일(ELF 포장, 안에 PE 페이로드).
    # md() 가 BSJB 를 직접 찾으므로 ELF 래퍼째로 읽는다. .pdb.so 는 제외.
    if not out:
        for n in sorted(z.namelist()):
            p_=n.split('/')
            if len(p_)==3 and p_[0]=='lib' and p_[2].startswith('lib_xkxk.frameworks.ui') and p_[2].endswith('.dll.so'):
                try: b=z.read(n); m=md(b)
                except Exception: m=None
                if m: out.setdefault(p_[1],{})[m['mod']]=(b,m)
    return out
def fromdir(path):
    D={}
    for f in glob.glob(os.path.join(path,'**','xkxk.frameworks.ui*'),recursive=True):
        if f.endswith(('.dll','.wasm')) and '/ref/' not in f and '/refint/' not in f:
            b=open(f,'rb').read(); m=md(b)
            if m:
                n=m['mod']
                m['size']=len(b); m['path']=f; D.setdefault(n,(b,m))
    return {'.':D}
def check(label,D):
    pd={k:v[1] for k,v in D.items() if k.startswith('xkxk.frameworks.ui')}
    tset={};
    for k,m in pd.items():
        for ns,nm,_,_ in m['td']: tset[(ns,nm)]=k
    names={nm for (ns,nm) in tset}
    bad=[]; mbad=[]; nref=0; mcheck=0
    for k,m in pd.items():
        own={(ns,nm) for ns,nm,_,_ in m['td']}
        for i,(sc,idx,ns,nm) in enumerate(m['tr']):
            if sc==2 and ns.startswith('xkxk.frameworks.ui'):
                nref+=1
                if (ns,nm) not in tset: bad.append(f'{k}->{ns}.{nm}')
            elif sc==3:
                # 중첩 typeref: 바깥이 Pd 이면 이름만 검사
                o=m['tr'][idx-1] if idx-1<len(m['tr']) else None
                if o and o[2].startswith('xkxk.frameworks.ui') and o[0]==2:
                    nref+=1
                    if nm not in names: bad.append(f'{k}->{o[2]}.{o[3]}/{nm}')
        for c,idx,nm in m['mr']:
            if c==1 and idx-1<len(m['tr']):
                sc,_,ns,tn=m['tr'][idx-1]
                if sc==2 and ns.startswith('xkxk.frameworks.ui') and (ns,tn) in tset:
                    mcheck+=1
                    if nm not in pd[tset[(ns,tn)]]['members'].get((ns,tn),set()): mbad.append(f'{k}->{ns}.{tn}::{nm}')
    print(f'[{label}] Pd어셈블리={len(pd)} typeref검사={nref} 누락={len(set(bad))} memberref검사={mcheck} 누락={len(set(mbad))}')
    for k,m in sorted(pd.items()):
        print(f'    {k:18} mvid={m["mvid"][:8]} types={m["typedefs"]} methods={m["methods"]} ProfileStores={"Y" if ("xkxk.frameworks.ui.data.Layout","ProfileStores") in tset and tset[("xkxk.frameworks.ui.data.Layout","ProfileStores")]==k else "-"} tref={"Y" if any(x[3]=="ProfileStores" and x[0]==2 for x in m["tr"]) else "-"} diagmark={"Y" if ("xkxk.frameworks.ui.data.Samples","GridSeedLegacy") in [(a,b) for a,b,_,_ in m["td"]] else "-"}')
    for x in sorted(set(bad))[:15]: print('    MISSING-TYPE',x)
    for x in sorted(set(mbad))[:15]: print('    MISSING-MEMBER',x)
    return len(set(bad)) + len(set(mbad)) if pd else -1
def selftest():
    # 합성 데이터 검사: (1) zstd 프레임 원시·RLE 블록 (2) XABA 스토어 파싱+XAZS 풀기. 압축 블록 경로는 실제 APK 로만 검증된다(README 의 확인 절차).
    global md
    def blk(last, ty, size): return (last | ty << 1 | size << 3).to_bytes(3, 'little')
    fr = b'\x28\xb5\x2f\xfd' + bytes([0x20]) + bytes([8]) + blk(0, 0, 3) + b'abc' + blk(1, 1, 5) + b'x'
    assert zstd_decompress(fr) == b'abcxxxxx', 'zstd 원시/RLE 블록'
    z = b'XAZS' + struct.pack('<II', 0, 8) + fr
    names = ['xkxk.frameworks.ui.Test.dll', 'System.Foo.dll']; datas = [z, b'MZ-raw']
    idx = b''.join(struct.pack('<IIB', 0x1000 + i, i, 0) for i in range(4))
    nb = b''.join(struct.pack('<I', len(n)) + n.encode() for n in names)
    base = 20 + len(idx) + 28 * 2 + len(nb); descs = b''; blob = b''
    for i, dd in enumerate(datas):
        descs += struct.pack('<7I', i, base + len(blob), len(dd), 0, 0, 0, 0); blob += dd
    st = b'\x7fELF' + b'\0' * 12 + struct.pack('<5I', 0x41424158, 0x80010003, 2, 4, len(idx)) + idx + descs + nb + blob
    old = md
    try:
        md = lambda b: {'mod': 'xkxk.frameworks.ui.Test.dll', 'raw': b}
        got = store_entries(st, want=lambda n: n.startswith('xkxk.frameworks.ui'))
    finally: md = old
    assert list(got) == ['xkxk.frameworks.ui.Test.dll'] and got['xkxk.frameworks.ui.Test.dll'][0] == b'abcxxxxx', 'XABA 스토어 파싱'
    print('self-test OK'); return 0
def main(a):
    if a[:1] == ['--self-test']: return selftest()
    if not a:
        print(__doc__ or 'usage'); return 2
    if len(a) % 2 == 0 and not any(os.path.exists(x) for x in a[0::2]):
        pairs = list(zip(a[0::2], a[1::2]))
    else:
        pairs = [(os.path.basename(x.rstrip('/')) or x, x) for x in a]
    rc = 0
    for lab, p in pairs:
        if not os.path.exists(p):
            print('[없음]', lab, p); rc = max(rc, 2); continue
        if p.endswith('.apk'):
            res = fromapk(p)
            if not res:
                print('[어셈블리 없음] %s — 본 레이아웃: lib/<abi>/libassembly-store.so(Release 스토어), lib/<abi>/lib_xkxk.frameworks.ui*.dll.so(Debug 개별) 모두 없음' % lab); rc = max(rc, 2)
            for abi, D in res.items():
                n = check(lab + ':' + abi, D)
                rc = max(rc, 2 if n < 0 else (1 if n else 0))
        else:
            n = check(lab, fromdir(p)['.'])
            rc = max(rc, 2 if n < 0 else (1 if n else 0))
    print('closure_rc=%d' % rc)
    return rc
if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
