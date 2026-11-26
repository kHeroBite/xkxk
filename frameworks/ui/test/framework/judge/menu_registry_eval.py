# 메뉴 레지스트리 판정기 C# 식 값 계산·정의(const·필드·도우미) 수집(menu_registry_check 분할 모듈)
from menu_registry_lex import *  # noqa: F401,F403


def mentions_row(node, helpers):
    """식 안에 표 행 생성(new ShellMenuRow·인자 12 의 new(…)·행 도우미 호출)이 있는가 — 무관한 new 식의 해석 실패는 사유로 내지 않는다."""
    if not isinstance(node, tuple) or not node:
        return False
    k = node[0]
    if k == "ctor" and (node[1] == "ShellMenuRow" or (node[1] is None and len(node[2]) == len(ROW_F))):
        return True
    if k == "call" and helpers.get(node[1], ("",))[0] == "ShellMenuRow":
        return True
    if k in ("ctor", "call"):
        return any(mentions_row(e, helpers) for _, e in node[2])
    if k == "list":
        return any(mentions_row(e, helpers) for e in node[1])
    return k == "with" and mentions_row(node[1], helpers)


class Evaluator:
    """노드 값을 계산한다. names: 상수·필드 노드, helpers: 이름→(반환형, 매개변수, 본문), natives: 파이썬 함수."""

    def __init__(self, names=None, helpers=None, natives=None, cmd_keys=None):
        self.names, self.helpers, self.natives = names or {}, helpers or {}, natives or {}
        self.cmd_keys, self.cache, self.busy = cmd_keys or {}, {}, set()

    def name(self, n, env):
        if n in env:
            return env[n]
        last = n.rsplit(".", 1)[-1]
        if n.startswith("ShellMenuRowKind."):
            return last
        if n in ICON_EXPR:
            return IconRef(n)
        if n.startswith("CmdIconKeys."):
            return self.cmd_keys.get(last, Unresolved(n))
        for k in (n, last):
            if k in self.cache:
                return self.cache[k]
            if k in self.names and k not in self.busy:
                self.busy.add(k)
                try:
                    self.cache[k] = self.eval(self.names[k], {})
                finally:
                    self.busy.discard(k)
                return self.cache[k]
        raise EvalError("알 수 없는 이름 '%s'" % n)

    def record(self, fields, args, env, where):
        vals, pos = {}, 0
        for nm, e in args:
            if nm is None:
                if pos >= len(fields):
                    raise EvalError("인자가 너무 많음(%s)" % where)
                vals[fields[pos]] = self.eval(e, env)
                pos += 1
            else:
                vals[nm] = self.eval(e, env)
        miss = [f for f in fields if f not in vals]
        if miss:
            raise EvalError("인자 없음 %s(%s)" % (miss[:3], where))
        vals["_t"] = "row" if fields is ROW_F else "mob"
        return vals

    def eval(self, node, env):
        k = node[0]
        if k == "lit":
            return node[1]
        if k == "list":
            return [self.eval(e, env) for e in node[1]]
        if k == "cat":
            a, b = self.eval(node[1], env), self.eval(node[2], env)
            if not isinstance(a, str) or not isinstance(b, str):
                raise EvalError("문자열이 아닌 + 연결")
            return a + b
        if k == "name":
            return self.name(node[1], env)
        if k == "with":
            base = self.eval(node[1], env)
            if not isinstance(base, dict):
                raise EvalError("with 대상이 레코드가 아님")
            out = dict(base)
            out.update({f: self.eval(e, env) for f, e in node[2].items()})
            return out
        where = "%s:%s" % (node[4], node[3])
        if k == "ctor":
            t, a = node[1], node[2]
            if t == "ShellMenuRow" or (t is None and len(a) == len(ROW_F)):
                return self.record(ROW_F, a, env, where)
            if t == "ShellMenuMobileRow" or (t is None and len(a) == len(MOB_F)):
                return self.record(MOB_F, a, env, where)
            raise EvalError("모르는 생성식 new %s(인자 %d)(%s)" % (t or "", len(a), where))
        if k == "call":
            n, a = node[1], node[2]
            if n == "Array.Empty":
                return []
            if n in self.natives:
                return self.natives[n](*[self.eval(e, env) for _, e in a])
            if n not in self.helpers:
                raise EvalError("모르는 호출 %s(%s)" % (n, where))
            _, params, body = self.helpers[n]
            local, pos, rest = {}, 0, []
            for nm, e in a:
                v = self.eval(e, env)
                if nm is not None:
                    local[nm] = v
                elif pos < len(params) and not params[pos][2]:
                    local[params[pos][0]] = v
                    pos += 1
                else:
                    rest.append(v)
            for pname, default, is_params in params:
                if is_params:
                    local[pname] = rest
                elif pname not in local:
                    if default is None:
                        raise EvalError("도우미 %s 인자 %s 없음(%s)" % (n, pname, where))
                    local[pname] = self.eval(default, {})
            return self.eval(body, local)
        raise EvalError("모르는 노드 %s" % k)


def end_of(toks, i, stop=";"):
    """i 부터 깊이 0 의 stop 토큰 위치."""
    depth = 0
    while i < len(toks):
        v = toks[i][1]
        if toks[i][0] == "op":
            if v == stop and depth == 0:
                return i
            if v in "([{":
                depth += 1
            elif v in ")]}":
                depth -= 1
        i += 1
    return i


def defs(toks):
    """(상수·필드 노드, 도우미, 행 탐색에서 뺄 구간) — const string/int, static readonly 필드, ShellMenuRow·ShellMenuMobileRow 반환 도우미."""
    names, helpers, skip = {}, {}, []
    for i, (kind, v, _, _) in enumerate(toks):
        if kind != "id":
            continue
        if v == "const" and i + 3 < len(toks) and toks[i + 2][0] == "id" and toks[i + 3][1] == "=":
            j = end_of(toks, i + 4, ",")
            j = min(j, end_of(toks, i + 4))
            try:
                names[toks[i + 2][1]] = Parser(toks[i + 4:j]).expr()
            except EvalError:
                pass
        elif v == "readonly":
            j = i + 1
            while j + 1 < len(toks) and not (toks[j][0] == "id" and toks[j + 1][1] == "=") and toks[j][1] != ";":
                j += 1
            if j + 1 >= len(toks) or toks[j + 1][1] != "=":
                continue
            typ = " ".join(t[1] for t in toks[i + 1:j])
            e = end_of(toks, j + 2)
            try:
                names[toks[j][1]] = Parser(toks[j + 2:e]).expr()
            except EvalError:
                pass
            if typ.rstrip("?").strip() in ("ShellMenuRow", "ShellMenuMobileRow"):
                skip.append((i, e))
        elif v in ("ShellMenuRow", "ShellMenuMobileRow") and i > 0 and toks[i - 1][1] not in ("new", "record"):
            q = i + 2 if i + 1 < len(toks) and toks[i + 1][1] == "?" else i + 1  # 반환형 뒤 ? 허용
            if not (q + 1 < len(toks) and toks[q][0] == "id" and toks[q + 1][1] == "("):
                continue
            close = end_of(toks, q + 2, ")")
            params = []
            for part in split_commas(toks[q + 2:close]):
                is_p = bool(part) and part[0][1] == "params"
                eq = next((k for k, t in enumerate(part) if t[1] == "="), None)
                head = part[:eq] if eq is not None else part
                default = Parser(part[eq + 1:]).expr() if eq is not None else None
                params.append((head[-1][1], default, is_p))
            if toks[close + 1][1] == "=>":
                e = end_of(toks, close + 2)
                body = Parser(toks[close + 2:e]).expr()
            else:
                r = next(k for k in range(close, len(toks)) if toks[k][1] == "return")
                e = end_of(toks, r + 1)
                body = Parser(toks[r + 1:e]).expr()
                e = end_of(toks, e, "}")
            helpers[toks[q][1]] = (v, params, body)
            skip.append((i, e))
    return names, helpers, skip


def split_commas(toks):
    parts, cur, depth = [], [], 0
    for t in toks:
        if t[0] == "op" and t[1] in "([{<":
            depth += 1
        elif t[0] == "op" and t[1] in ")]}>":
            depth -= 1
        if t[1] == "," and depth == 0:
            parts.append(cur)
            cur = []
        else:
            cur.append(t)
    if cur:
        parts.append(cur)
    return parts
