# Publicar el juego en GitHub Pages

1. Sube los cambios del repositorio, incluidos `.github/workflows/pages.yml`,
   `scripts/build-pages.mjs`, `index.html` y los archivos `Builds.*` originales.
   Estos archivos deben quedar en la raíz del repositorio, no en una subcarpeta.
2. En GitHub abre **Settings > Pages > Build and deployment > Source**
   y selecciona **GitHub Actions**.
3. Haz push a `main` o `master`. Si ya subiste los cambios antes de activar Pages,
   abre **Actions > Deploy Unity game to GitHub Pages > Run workflow**.
4. Al terminar, el paso de despliegue y Settings > Pages mostrarán la URL pública.

El proceso descomprime los archivos Brotli de Unity en `dist/` y adapta el HTML.
Así el servidor no necesita enviar cabeceras especiales para `.br`.
Conserva los `.br` originales en Git; no subas `dist/`.

Para generar el sitio localmente, con Node.js 22 o posterior:

```sh
node scripts/build-pages.mjs
```

Sirve la carpeta `dist/` con un servidor HTTP estático para probarla.
No abras el HTML con doble clic. El HTML de la raíz sigue usando los `.br`
originales y requiere un servidor con soporte de Brotli; Pages usa el de `dist/`.

Esto publica el build de Unity existente. Los cambios a scripts C# o escenas
requieren exportar nuevamente el juego desde Unity y reemplazar los `Builds.*`.
