// 배경 이미지 등 바이트 blob 을 IndexedDB 에 두는 저장소(09/21 §6.2) — C# BrowserBlobStore 가 JSImport 로 부른다. 바이트는 base64 문자열로 주고받는다
const DB_NAME = "pd-blobs";
const STORE = "blobs";
const VERSION = 1;
let dbPromise = null;

function openDb() {
  if (!dbPromise) {
    dbPromise = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, VERSION);
      req.onupgradeneeded = () => {
        if (!req.result.objectStoreNames.contains(STORE)) {
          req.result.createObjectStore(STORE);
        }
      };
      req.onsuccess = () => {
        const db = req.result;
        db.onversionchange = () => { db.close(); dbPromise = null; };
        resolve(db);
      };
      req.onerror = () => { dbPromise = null; reject(req.error); };
    });
  }

  return dbPromise;
}

// 트랜잭션이 끝나야(oncomplete) 성공으로 본다 — 할당량 초과(QuotaExceededError)는 put 이 아니라 트랜잭션 중단으로 올 수 있다
function run(mode, action) {
  return openDb().then(db => new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const req = action(tx.objectStore(STORE));
    let result;
    req.onsuccess = () => { result = req.result; };
    tx.oncomplete = () => resolve(result);
    tx.onerror = () => reject(tx.error || req.error);
    tx.onabort = () => reject(tx.error || req.error || new DOMException("aborted", "AbortError"));
  }));
}

// C# 쪽은 메시지 문자열만 본다 — 오류 이름(QuotaExceededError 등)을 앞에 붙여 넘긴다
function wrap(e) {
  const name = e && e.name ? e.name : "Error";
  return new Error(name + ": " + (e && e.message ? e.message : String(e)));
}

function toBase64(bytes) {
  let s = "";
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    s += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
  }

  return btoa(s);
}

function fromBase64(b64) {
  const s = atob(b64);
  const out = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) {
    out[i] = s.charCodeAt(i);
  }

  return out;
}

/** 없으면 null(지워짐·브라우저 축출). */
export async function read(key) {
  try {
    const v = await run("readonly", s => s.get(key));
    if (v === undefined || v === null) {
      return null;
    }

    return toBase64(v instanceof Uint8Array ? v : new Uint8Array(v));
  } catch (e) {
    throw wrap(e);
  }
}

export async function write(key, b64) {
  try {
    await run("readwrite", s => s.put(fromBase64(b64), key));
  } catch (e) {
    throw wrap(e);
  }
}

export async function remove(key) {
  try {
    await run("readwrite", s => s.delete(key));
  } catch (e) {
    throw wrap(e);
  }
}
