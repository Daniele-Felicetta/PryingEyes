/* Prying Eyes wiki: the sidebar, the mode filter and the search of the static site (tools/wiki_site.py).
   Works from file:// too: the index is a plain script (assets/search-index.js), no fetch and no network. */
(function () {
  "use strict";
  var body = document.body;
  var root = body.getAttribute("data-root") || "";
  var current = body.getAttribute("data-current") || "";
  var nav = document.getElementById("nav");
  var input = document.getElementById("q");
  var results = document.getElementById("results");
  var entries = window.WIKI_SEARCH || [];
  var filter = "all";
  var CLASSES = [
    ["hub", "Guides"], ["mode", "Modes"], ["structure", "Structures"], ["entity", "Entities"],
    ["object", "Objects"], ["system", "Systems"], ["pipeline", "Pipeline and tools"], ["project", "Projects and history"]
  ];

  function fold(text) {
    return (text || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();
  }

  function shown(entry) {
    if (filter === "all" || entry.m === "none" || entry.m === "" || entry.m === "both") return true;
    return entry.m === filter;
  }

  function make(tag, attrs, text) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (key) { node.setAttribute(key, attrs[key]); });
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function byTitle(a, b) {
    return a.t.localeCompare(b.t);
  }

  function buildNav() {
    if (!nav) return;
    nav.textContent = "";
    CLASSES.forEach(function (group) {
      var members = entries.filter(function (e) { return e.c === group[0]; });
      if (!members.length) return;
      var details = make("details", {});
      if (members.some(function (e) { return e.p === current; })) details.open = true;
      details.appendChild(make("summary", {}, group[1] + " (" + members.length + ")"));
      var kinds = [];
      members.forEach(function (e) { if (kinds.indexOf(e.k) < 0) kinds.push(e.k); });
      kinds.forEach(function (kind) {
        details.appendChild(make("h4", { "data-kind": kind }, kind));
        var list = make("ul", {});
        members.filter(function (e) { return e.k === kind; }).sort(byTitle).forEach(function (e) {
          var item = make("li", { "data-mode": e.m });
          var link = make("a", { href: root + e.u }, e.t);
          if (e.p === current) link.setAttribute("aria-current", "page");
          item.appendChild(link);
          list.appendChild(item);
        });
        details.appendChild(list);
      });
      nav.appendChild(details);
    });
    applyFilter();
  }

  function applyFilter() {
    if (nav) {
      nav.querySelectorAll("li[data-mode]").forEach(function (item) {
        var mode = item.getAttribute("data-mode");
        var keep = filter === "all" || mode === "none" || mode === "" || mode === "both" || mode === filter;
        item.hidden = !keep;
      });
      nav.querySelectorAll("details").forEach(function (details) {
        var any = details.querySelector("li:not([hidden])");
        details.hidden = !any;
      });
    }
    document.querySelectorAll("[data-mode-filter]").forEach(function (button) {
      button.setAttribute("aria-pressed", String(button.getAttribute("data-mode-filter") === filter));
    });
    search();
  }

  function search() {
    if (!results || !input) return;
    var words = fold(input.value).split(/\s+/).filter(Boolean);
    results.textContent = "";
    if (words.length === 0) { results.hidden = true; return; }
    var hits = entries.filter(function (e) {
      if (!shown(e)) return false;
      var haystack = fold(e.t + " " + e.s + " " + e.k);
      return words.every(function (w) { return haystack.indexOf(w) >= 0; });
    }).sort(byTitle).slice(0, 14);
    if (hits.length === 0) {
      results.appendChild(make("small", {}, "No page matches."));
    }
    hits.forEach(function (e) {
      var link = make("a", { href: root + e.u });
      link.appendChild(document.createTextNode(e.t));
      link.appendChild(make("small", {}, e.k + (e.s ? " · " + e.s : "")));
      results.appendChild(link);
    });
    results.hidden = false;
  }

  document.querySelectorAll("[data-mode-filter]").forEach(function (button) {
    button.addEventListener("click", function () {
      filter = button.getAttribute("data-mode-filter");
      applyFilter();
    });
  });
  if (input) {
    input.addEventListener("input", search);
    input.addEventListener("keydown", function (event) {
      if (event.key === "Escape") { input.value = ""; search(); input.blur(); }
    });
    document.addEventListener("click", function (event) {
      if (results && !results.contains(event.target) && event.target !== input) results.hidden = true;
    });
    input.addEventListener("focus", search);
  }
  buildNav();
})();
