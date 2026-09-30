#!/usr/bin/env python3
"""Render docs/*.md into navigable, self-contained HTML pages (docs/*.html).

Usage:
    python3 scripts/build_docs.py          # (re)generate docs/*.html
    python3 scripts/build_docs.py --check  # exit 1 if any HTML is out of date (for CI)

Requires: pip install -r scripts/requirements-docs.txt
Output is deterministic (no timestamps) so `--check` can run in CI.
Mermaid diagrams and code highlighting are rendered client-side from a CDN.
"""
from __future__ import annotations

import argparse
import html
import re
import sys
from pathlib import Path

try:
    import markdown
except ImportError:  # pragma: no cover
    sys.exit("Missing dependency: run `python3 -m pip install markdown`")

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
REPO_URL = "https://github.com/alba-alonso-dev/leaderboard-api"

# Order defines the sidebar navigation: (path relative to docs/, label, hint, group).
PAGES = [
    ("index.md", "Inicio", "Índice de la documentación", "Diseño"),
    ("requirements.md", "Requerimientos", "RF, RNF e historias de usuario", "Diseño"),
    ("architecture.md", "Arquitectura", "Capas, datos, endpoints y seguridad", "Diseño"),
    ("roadmap-and-tasks.md", "Roadmap y tareas", "Fases Noviembre → Marzo", "Diseño"),
    ("coding-standards.md", "Estándares de código", "Convenciones, patrones y testing", "Diseño"),
    ("performance.md", "Rendimiento", "Benchmark k6 y planes de ejecución", "Resultados"),
    ("adr/0001-postgresql.md", "ADR-001 · PostgreSQL", "Almacén principal", "Decisiones (ADR)"),
    ("adr/0002-clean-architecture.md", "ADR-002 · Clean Architecture", "Estructura en capas", "Decisiones (ADR)"),
    ("adr/0003-anti-cheat.md", "ADR-003 · Anti-cheat", "HMAC, nonce, rate limiting", "Decisiones (ADR)"),
    ("adr/0004-future-improvements.md", "ADR-004 · Mejoras futuras", "Redis, tiempo real, microservicios", "Decisiones (ADR)"),
]

CSS = """
:root {
  --bg: #ffffff; --bg-soft: #f6f8fa; --bg-sidebar: #f8fafc; --fg: #1f2328; --fg-muted: #57606a;
  --border: #d0d7de; --accent: #4f46e5; --accent-soft: #eef2ff; --code-bg: #f3f4f6;
  --quote: #6366f1; --ok: #1a7f37; --shadow: 0 1px 3px rgba(0,0,0,.08);
  --sidebar-w: 17rem; --content-w: 60rem;
  color-scheme: light;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg: #0d1117; --bg-soft: #161b22; --bg-sidebar: #0f141b; --fg: #e6edf3; --fg-muted: #9198a1;
    --border: #30363d; --accent: #a5b4fc; --accent-soft: #1e1b4b; --code-bg: #161b22;
    --quote: #818cf8; --ok: #3fb950; --shadow: none; color-scheme: dark;
  }
}
:root[data-theme="dark"] {
  --bg: #0d1117; --bg-soft: #161b22; --bg-sidebar: #0f141b; --fg: #e6edf3; --fg-muted: #9198a1;
  --border: #30363d; --accent: #a5b4fc; --accent-soft: #1e1b4b; --code-bg: #161b22;
  --quote: #818cf8; --ok: #3fb950; --shadow: none; color-scheme: dark;
}
* { box-sizing: border-box; }
html { scroll-padding-top: 1rem; }
body {
  margin: 0; background: var(--bg); color: var(--fg);
  font: 16px/1.65 system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
}
a { color: var(--accent); text-underline-offset: 2px; }
a:focus-visible, button:focus-visible { outline: 3px solid var(--accent); outline-offset: 2px; border-radius: 4px; }
.skip-link { position: absolute; left: -999px; top: .5rem; background: var(--accent); color: var(--bg); padding: .5rem 1rem; z-index: 10; }
.skip-link:focus { left: .5rem; }

.layout { display: grid; grid-template-columns: var(--sidebar-w) minmax(0, 1fr); min-height: 100vh; }
.sidebar {
  position: sticky; top: 0; height: 100vh; overflow-y: auto; padding: 1.5rem 1.25rem;
  background: var(--bg-sidebar); border-right: 1px solid var(--border); font-size: .92rem;
}
.brand { display: block; font-weight: 700; font-size: 1.05rem; color: var(--fg); text-decoration: none; margin-bottom: .25rem; }
.brand-sub { color: var(--fg-muted); font-size: .8rem; margin: 0 0 1.25rem; }
.sidebar h2 { font-size: .72rem; text-transform: uppercase; letter-spacing: .08em; color: var(--fg-muted); margin: 1.25rem 0 .5rem; border: 0; padding: 0; }
.sidebar ul { list-style: none; margin: 0; padding: 0; }
.nav a { display: block; padding: .4rem .6rem; border-radius: 6px; color: var(--fg); text-decoration: none; }
.nav a small { display: block; color: var(--fg-muted); font-size: .75rem; }
.nav a:hover { background: var(--bg-soft); }
.nav a[aria-current="page"] { background: var(--accent-soft); color: var(--accent); font-weight: 600; }
.toc ul ul { padding-left: .8rem; }
.toc a { display: block; padding: .15rem 0; color: var(--fg-muted); text-decoration: none; font-size: .84rem; }
.toc a:hover { color: var(--accent); }
.toc > ul > li > ul > li > ul { display: none; }
.theme-toggle {
  margin-top: 1.5rem; width: 100%; padding: .45rem; border-radius: 6px; cursor: pointer;
  border: 1px solid var(--border); background: var(--bg); color: var(--fg); font: inherit; font-size: .85rem;
}

main { padding: 2.5rem 3rem 4rem; min-width: 0; }
.content { max-width: var(--content-w); }
h1, h2, h3, h4 { line-height: 1.3; scroll-margin-top: 1rem; }
h1 { font-size: 2.1rem; margin-top: 0; }
h2 { font-size: 1.5rem; margin-top: 2.75rem; padding-bottom: .35rem; border-bottom: 1px solid var(--border); }
h3 { font-size: 1.2rem; margin-top: 2rem; }
.headerlink { opacity: 0; margin-left: .35rem; text-decoration: none; font-weight: 400; }
h1:hover .headerlink, h2:hover .headerlink, h3:hover .headerlink, h4:hover .headerlink, .headerlink:focus { opacity: .6; }
hr { border: 0; border-top: 1px solid var(--border); margin: 2.5rem 0; }
blockquote { margin: 1.25rem 0; padding: .75rem 1rem; border-left: 4px solid var(--quote); background: var(--bg-soft); border-radius: 0 6px 6px 0; }
blockquote p { margin: .25rem 0; }
code { font-family: ui-monospace, SFMono-Regular, "Cascadia Code", Menlo, Consolas, monospace; font-size: .86em; background: var(--code-bg); padding: .12em .35em; border-radius: 4px; }
pre { background: var(--code-bg); border: 1px solid var(--border); border-radius: 8px; padding: 1rem; overflow-x: auto; line-height: 1.5; }
pre code { background: none; padding: 0; font-size: .84rem; }
pre code.hljs { background: none; padding: 0; }
pre.mermaid { background: var(--bg); text-align: center; }
.table-wrap { overflow-x: auto; margin: 1.25rem 0; border: 1px solid var(--border); border-radius: 8px; }
table { border-collapse: collapse; width: 100%; font-size: .9rem; }
th, td { padding: .55rem .75rem; border-bottom: 1px solid var(--border); text-align: left; vertical-align: top; }
th { background: var(--bg-soft); font-weight: 600; white-space: nowrap; }
tr:last-child td { border-bottom: 0; }
tbody tr:hover { background: var(--bg-soft); }
li.task { list-style: none; margin-left: -1.4rem; }
li.task input { margin-right: .45rem; accent-color: var(--ok); }
.page-footer { margin-top: 4rem; padding-top: 1rem; border-top: 1px solid var(--border); color: var(--fg-muted); font-size: .82rem; display: flex; flex-wrap: wrap; gap: .5rem 1.5rem; justify-content: space-between; }

@media (max-width: 900px) {
  .layout { grid-template-columns: 1fr; }
  .sidebar { position: static; height: auto; border-right: 0; border-bottom: 1px solid var(--border); padding: 1rem; }
  .toc { display: none; }
  main { padding: 1.5rem 1rem 3rem; }
  h1 { font-size: 1.7rem; }
}
@media print {
  .sidebar, .skip-link, .headerlink { display: none; }
  .layout { display: block; }
}
"""

SCRIPT = """
<script>
  (function () {
    var root = document.documentElement;
    try { var saved = localStorage.getItem('docs-theme'); if (saved) root.dataset.theme = saved; } catch (e) {}
    var btn = document.getElementById('theme-toggle');
    if (btn) btn.addEventListener('click', function () {
      var dark = root.dataset.theme ? root.dataset.theme === 'dark'
                                    : matchMedia('(prefers-color-scheme: dark)').matches;
      root.dataset.theme = dark ? 'light' : 'dark';
      try { localStorage.setItem('docs-theme', root.dataset.theme); } catch (e) {}
      location.reload(); // re-render Mermaid with the matching theme
    });
  })();
</script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/highlight.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/languages/csharp.min.js"></script>
<script src="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/languages/gherkin.min.js"></script>
<script>
  if (window.hljs) document.querySelectorAll('pre code[class*="language-"]').forEach(function (el) { hljs.highlightElement(el); });
</script>
<script type="module">
  import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';
  const root = document.documentElement;
  const dark = root.dataset.theme ? root.dataset.theme === 'dark'
                                  : matchMedia('(prefers-color-scheme: dark)').matches;
  mermaid.initialize({ startOnLoad: true, theme: dark ? 'dark' : 'default', securityLevel: 'strict' });
</script>
"""

TEMPLATE = """<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="{description}">
<title>{title} · Leaderboard API</title>
<link rel="stylesheet" media="(prefers-color-scheme: light)" href="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github.min.css">
<link rel="stylesheet" media="(prefers-color-scheme: dark)" href="https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.9.0/styles/github-dark.min.css">
<style>{css}</style>
</head>
<body>
<a class="skip-link" href="#main">Saltar al contenido</a>
<div class="layout">
  <aside class="sidebar" aria-label="Navegación de la documentación">
    <a class="brand" href="{root}index.html">🏆 Leaderboard API</a>
    <p class="brand-sub">Documentación de diseño</p>
    <nav class="nav" aria-label="Documentos">
{nav}
    </nav>
    <nav class="toc" aria-label="En esta página">
      <h2>En esta página</h2>
{toc}
    </nav>
    <button class="theme-toggle" id="theme-toggle" type="button">◐ Cambiar tema</button>
  </aside>
  <main id="main">
    <article class="content">
{body}
    </article>
    <footer class="page-footer">
      <span>Generado desde <a href="{source_name}"><code>docs/{source}</code></a> con <code>scripts/build_docs.py</code>. No editar este HTML a mano.</span>
      <span><a href="{repo}">Repositorio</a> · <a href="{repo}/blob/main/README.md">README</a></span>
    </footer>
  </main>
</div>
{script}
</body>
</html>
"""

MERMAID_RE = re.compile(r'<pre><code class="language-mermaid">(.*?)</code></pre>', re.S)
HREF_RE = re.compile(r'href="([^"#]+\.md)(#[^"]*)?"')
TABLE_RE = re.compile(r"(<table>.*?</table>)", re.S)
TASK_RE = re.compile(r"<li>(<p>)?\[([ xX])\]\s*")


def rewrite_link(page_dir: Path, target: str, fragment: str) -> str:
    """Links between documentation pages point to the generated HTML; links outside docs/ point to GitHub.
    Links written as `./file.md` are explicit links to the Markdown source and are kept."""
    if target.startswith(("http://", "https://", "./")):
        return f'href="{target}{fragment}"'
    resolved = (page_dir / target).resolve()
    if resolved.is_relative_to(DOCS):
        return f'href="{target[:-3]}.html{fragment}"'
    return f'href="{REPO_URL}/blob/main/{resolved.relative_to(ROOT).as_posix()}{fragment}"'


def render_nav(current: str) -> str:
    depth = current.count("/")
    root = "../" * depth
    lines, group = [], None
    for path, label, hint, page_group in PAGES:
        if page_group != group:
            if group is not None:
                lines.append("      </ul>")
            lines.append(f"      <h2>{html.escape(page_group)}</h2>\n      <ul>")
            group = page_group
        attr = ' aria-current="page"' if path == current else ""
        lines.append(
            f'        <li><a href="{root}{path[:-3]}.html"{attr}>{html.escape(label)}<small>{html.escape(hint)}</small></a></li>'
        )
    lines.append("      </ul>")
    return "\n".join(lines)


def render_page(md_file: Path) -> str:
    source = md_file.read_text(encoding="utf-8")
    md = markdown.Markdown(
        extensions=["extra", "sane_lists", "toc"],
        extension_configs={"toc": {"permalink": "#", "permalink_title": "Enlace a esta sección", "toc_depth": "2-3"}},
    )
    body = md.convert(source)

    body = MERMAID_RE.sub(lambda m: f'<pre class="mermaid">{m.group(1)}</pre>', body)
    body = HREF_RE.sub(lambda m: rewrite_link(md_file.parent, m.group(1), m.group(2) or ""), body)
    body = TABLE_RE.sub(r'<div class="table-wrap">\1</div>', body)
    body = TASK_RE.sub(
        lambda m: '<li class="task">' + (m.group(1) or "")
        + f'<input type="checkbox" disabled{" checked" if m.group(2) != " " else ""} aria-label="{"Completada" if m.group(2) != " " else "Pendiente"}"> ',
        body,
    )

    title_match = re.search(r"^#\s+(.+)$", source, re.M)
    title = title_match.group(1).strip() if title_match else md_file.stem
    relative = md_file.relative_to(DOCS).as_posix()
    page = next(p for p in PAGES if p[0] == relative)

    return TEMPLATE.format(
        title=html.escape(title),
        description=html.escape(page[2]),
        css=CSS,
        nav=render_nav(relative),
        root="../" * relative.count("/"),
        toc=md.toc,
        body=body,
        source=relative,
        source_name=md_file.name,
        repo=REPO_URL,
        script=SCRIPT,
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail if generated HTML differs from the committed files")
    args = parser.parse_args()

    stale = []
    for file, *_ in PAGES:
        md_file = DOCS / file
        out_file = md_file.with_suffix(".html")
        rendered = render_page(md_file)
        if args.check:
            if not out_file.exists() or out_file.read_text(encoding="utf-8") != rendered:
                stale.append(out_file.relative_to(ROOT))
        else:
            out_file.write_text(rendered, encoding="utf-8")
            print(f"✔ {md_file.relative_to(ROOT)} → {out_file.relative_to(ROOT)}")

    if stale:
        print("HTML out of date, run `python3 scripts/build_docs.py`:", *stale, sep="\n  ", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
