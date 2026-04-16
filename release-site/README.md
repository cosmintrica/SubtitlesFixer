# Release Site

Folderul acesta este separat de aplicatia WPF si poate fi publicat direct pe Vercel.

## Ce trebuie sa schimbi inainte de deploy

Editeaza `site.js` si pune:

- `downloadUrl`
- `donateUrl`

Optional, daca vrei alt nume / alt text legal:

- `version`
- `producer`
- `copyright`

## Deploy pe Vercel

1. creezi un proiect nou in Vercel
2. alegi acest repo sau acest folder
3. setezi `Root Directory` la `release-site`
4. framework preset poate ramane `Other`
5. `Build Command` gol
6. `Output Directory` gol
7. deploy

Pagina este statica, deci nu are nevoie de build tool sau dependinte.
