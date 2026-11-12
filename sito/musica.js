(function () {
  var filtri = document.querySelectorAll('[data-filtro]');
  var schede = document.querySelectorAll('[data-categoria]');
  var brani = document.querySelectorAll('[data-video]');
  var lettore = document.getElementById('lettore');
  var video = document.getElementById('video');
  var ultimoBrano;
  function ferma() {
    video.replaceChildren();
    lettore.hidden = true;
    brani.forEach(function (b) { b.setAttribute('aria-pressed', 'false'); });
  }
  filtri.forEach(function (filtro) {
    filtro.addEventListener('click', function () {
      filtri.forEach(function (f) { f.setAttribute('aria-pressed', String(f === filtro)); });
      schede.forEach(function (scheda) {
        scheda.hidden = filtro.dataset.filtro !== 'tutte' && filtro.dataset.filtro !== scheda.dataset.categoria;
      });
      ferma();
    });
  });
  brani.forEach(function (brano) {
    brano.addEventListener('click', function () {
      ferma();
      ultimoBrano = brano;
      document.getElementById('titolo-brano').textContent = brano.dataset.titolo;
      document.getElementById('apri-youtube').href = 'https://www.youtube.com/watch?v=' + brano.dataset.video;
      var frame = document.createElement('iframe');
      frame.src = 'https://www.youtube-nocookie.com/embed/' + brano.dataset.video;
      frame.title = brano.dataset.titolo;
      frame.allow = 'encrypted-media; picture-in-picture; fullscreen';
      frame.referrerPolicy = 'strict-origin-when-cross-origin';
      frame.allowFullscreen = true;
      video.appendChild(frame);
      lettore.hidden = false;
      brano.setAttribute('aria-pressed', 'true');
      var chiudi = lettore.querySelector('button');
      chiudi.focus({ preventScroll: true });
      lettore.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
    });
  });
  lettore.querySelector('button').addEventListener('click', function () {
    ferma();
    if (ultimoBrano) ultimoBrano.focus();
  });
})();