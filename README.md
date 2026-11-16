# R1 Keeper

Maza Windows fona programma, kas uztur lietojamu **Magicsee R1** Bluetooth pulti.

## Kāpēc tā vajadzīga

Magicsee R1 ir veidota Android un iOS ierīcēm. Uz Windows 11 tā strādā tikai uzreiz pēc
pārošanas. Kad pults aizmieg vai tiek izslēgta un pēc tam pieslēdzas no jauna, tā vēlreiz
atsūta pārošanas atslēgas, Windows to uzskata par kļūdu un savienojumu pārtrauc — un tā
bezgalīgi, kamēr pulti ar roku neizdzēš no Bluetooth ierīcēm un nesapāro no jauna.

R1 Keeper šo izdzēšanu un pārošanu izdara pati, bez klikšķiem, katru reizi, kad pults
parādās. Draiveri un administratora tiesības nav vajadzīgas.

## Lietošana

1. Uzbūvē: `dotnet publish src/R1Keeper -c Release -o publish`
2. Palaid `publish\R1Keeper.exe`. Paziņojumu joslā parādās apaļa ikona.
3. Ieslēdz pulti. Pirmajā reizē programma iegaumē pirmo ieraudzīto `Magicsee R1` un turpmāk
   rīkojas tikai ar to.
4. Pēc katras pults ieslēgšanas nospied **M + B** (spēļu režīms). Pults pēc ieslēgšanas
   vienmēr ir mūzikas režīmā, kurā kloķis un pogas maina skaļumu; programma to pamana un
   parāda atgādinājumu.

Ikonas krāsa: pelēka — pults nav redzama, oranža — notiek pārošana, zaļa — pults
pievienota, pelēka ar pauzes zīmi — pauzēts.

Izvēlne (labais klikšķis uz ikonas):

| Punkts | Ko dara |
|---|---|
| Pauzēt / Turpināt | Noņem pults pārošanu no Windows un aptur automātiku, lai pulti var lietot ar citu ierīci. |
| Aizmirst pulti | Aizmirst iegaumēto pulti; nākamā ieraudzītā `Magicsee R1` kļūst par programmas pulti. |
| Startēt kopā ar Windows | Ieslēdz vai izslēdz automātisku palaišanu. |
| Atvērt žurnālu | Atver žurnāla failu. |
| Iziet | Aizver programmu. Pults turpina strādāt līdz nākamajai atvienošanās reizei. |

## Spēļu režīma pogas

| Pults | Windows kontrolierī |
|---|---|
| Kloķis | X un Y ass, tikai galējie stāvokļi |
| A | pogas 5 un 10 reizē |
| B | poga 1 |
| C | pogas 4 un 9 reizē |
| D | poga 2 |
| Priekšējā augšējā | poga 8 |
| Priekšējā apakšējā | poga 7 |

## Kas zināms no pārbaudēm ar īsto pulti

Pārbaudīts 2026-11-16 uz Windows 11 Insider būvējuma 26300 ar vienu pulti.

- Pēc pults ieslēgšanas vai pamošanās tā ir lietojama apmēram 2 sekundēs. Ja pulti
  izslēdz un ieslēdz dažu sekunžu laikā, tas var aizņemt līdz 15 sekundēm.
- Pults pati aizmieg pēc apmēram 5 minūtēm bez pogu spiešanas; pamodina jebkura poga.
- Spēļu režīmu pults neatceras ne pēc izslēgšanas, ne pēc aizmigšanas. Pēc katras
  pamošanās jāspiež M + B. No datora režīmu pārslēgt nevar.
- Pirmais spiediens mūzikas režīmā nomaina skaļumu, pirms parādās atgādinājums.
- Dažas pogas spēļu režīmā nospiež divas kontroliera pogas reizē. Tā ir pults īpatnība;
  programma pogas nepārkartē.
- Apmēram minūti pēc katras pārošanas Windows piegādā pults sludinājumu, lai gan pults ir
  pievienota. Programma to ignorē; žurnālā tas redzams kā
  `advertisement ignored: the link is up`.
- Katra pārošana rada Windows ierīces pievienošanas skaņu.
- Nav pārbaudīts: datora miegs ar ieslēgtu programmu, Bluetooth izslēgšana un ieslēgšana,
  citi Windows būvējumi.

## Faili

- Žurnāls: `%LOCALAPPDATA%\R1Keeper\r1keeper.log`
- Iestatījumi: `%LOCALAPPDATA%\R1Keeper\settings.json`

`R1Keeper.exe --probe` neko nemaina: tas 15 sekundes ieraksta žurnālā, ko Windows zina par
pulti un kādi pults sludinājumi ir ēterā.

## Izstrāde

- `dotnet test` — pārošanas loģikas testi (bez pults).
- Dizains: `docs/superpowers/specs/2026-11-16-r1-keeper-design.md`
