# Plan - Cautare Automata Subtitrari si Fallback la Traducere

## Obiectiv

Sa putem procesa zeci sau sute de episoade dintr-un folder, nu cate un episod manual:

1. cautam automat subtitrare in romana pentru fiecare video
2. daca nu exista in romana, cautam in engleza
3. daca gasim doar engleza, o putem traduce in romana
4. totul intra in acelasi flux actual de preview, backup, restore si `.ro.srt`

## Ce exista deja

- detectie pe filme si seriale
- preview inainte de run
- mutare in backup
- restore
- normalizare de encoding si diacritice

Asta inseamna ca partea de "aplica fisierele local si sigur" exista deja. Lipseste doar partea de "gaseste subtitrari din afara".

## Varianta recomandata pentru cautare

### Provider principal

Recomandare: `OpenSubtitles.com API`

Motive:

- are API oficial
- suporta cautare dupa nume, sezon, episod, hash, limba
- este mai potrivit pentru automatizare decat scraping din site-uri random

### Provideri secundari

Optional, mai tarziu:

- `SubDL`
- alt provider cu API clar si rate-limit decent

Nu recomand ca prima varianta:

- scraping din site-uri fara API
- fluxuri fragile care se rup des

## Flux propus

### Etapa 1 - analiza locala

Pentru fiecare video:

- detectam daca deja exista `.ro.srt`
- extragem tipul: film sau serial
- pentru serial: sezon + episod
- pentru film: titlu + an, daca exista
- calculam si un hash de video pentru matching mai bun

### Etapa 2 - batch search

In loc sa cautam "manual per episod", aplicatia construieste o lista si o proceseaza automat:

- toate episoadele fara subtitrare finala
- toate episoadele unde vrei overwrite
- optional doar cele selectate de user

Pentru fiecare item:

1. cautare prioritara in `ro`
2. daca nu gaseste, cautare in `en`
3. ranking pe baza de:
   - hash
   - sezon / episod
   - release name
   - numar de download-uri / rating
   - hearing impaired / forced, daca vrem filtru

### Etapa 3 - download

Pentru potrivirile bune:

- descarcam subtitrarea in folder temporar
- o trecem prin motorul actual de decode / normalize
- o scriem final ca `.ro.srt`
- salvam sursa in `backup`

### Etapa 4 - fallback la traducere

Daca nu exista subtitrare in romana, dar exista in engleza:

- descarcam varianta engleza
- o traducem
- pastram timpii si structura SRT
- salvam rezultatul ca `.ro.srt`
- ideal pastram si originalul englez in backup

## Cum ar trebui sa arate in UI

Fara sa complicam mult:

- checkbox: `Cauta subtitrari online`
- checkbox: `Daca nu gasesc romana, incearca engleza`
- checkbox: `Daca gasesc doar engleza, traduce in romana`

In preview:

- `Gasita romana`
- `Gasita engleza - va fi tradusa`
- `Nu am gasit nimic`
- `Mai multe variante - alege manual`

## Traducere - optiuni reale

### Varianta buna si simpla

- `DeepL`
- `Google Cloud Translation`
- `Azure Translator`

Avantaje:

- calitate buna
- integrare usoara

Dezavantaje:

- cost
- subtitrarea pleaca la un serviciu extern

### Varianta offline / low-cost

- `Argos Translate`
- `Marian / OPUS-MT`
- `NLLB`

Avantaje:

- ruleaza local
- fara cost per request

Dezavantaje:

- setup mai greu
- calitate variabila
- viteza mai mica pe volume mari daca nu ai hardware bun

## Dificultate estimata

### 1. Cautare automata batch fara traducere

`Mediu spre greu`

Pentru un v1 bun:

- 2-4 zile pentru un POC
- 1-2 saptamani pentru ceva robust

Greu nu este UI-ul, ci:

- parsing bun pentru nume
- hashing
- rate limits
- ranking corect al subtitrarilor
- download sigur si retry logic

### 2. Fallback la traducere din engleza

`Mediu spre greu`

Pentru un v1 simplu cu API extern:

- inca 3-7 zile peste cautare

Pentru ceva foarte bun:

- batching
- protectie la HTML / italics / line breaks
- cost control
- retry / timeout / resume

### 3. Suport "orice limba"

`Greu`

Aici apar:

- detectie automata de limba
- ranking pe limba preferata
- fallback chain configurabil
- probleme de calitate la traducere
- UI mai complex

## Ordinea corecta de implementare

### Faza 1

- OpenSubtitles API
- cautare doar in romana
- preview + selectie manuala

### Faza 2

- fallback la cautare in engleza
- fara traducere inca

### Faza 3

- traducere EN -> RO
- preferabil configurabila

### Faza 4

- mai multi provideri
- mai multe limbi

## Riscuri

- rate limiting de la provider
- subtitrari nepotrivite dupa release
- diferente intre naming local si naming online
- termeni legali / ToS ai providerilor
- costuri de traducere daca volumul e mare

## Recomandarea mea

Cea mai buna ruta pentru proiectul tau este:

1. `OpenSubtitles API`
2. cautare batch pentru `ro`
3. fallback la `en`
4. traducere ca pas separat, activata doar daca userul vrea

Asa ramai cu un v1 realist, util si controlabil, fara sa transformi aplicatia intr-un monstru greu de mentinut.
