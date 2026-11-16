# R1 Keeper — dizains

Datums: 2026-11-16
Statuss: gaida apstiprinājumu

## Kas tas ir un kāpēc

Magicsee R1 ir Bluetooth LE pults (kloķis, pogas A–D, divas priekšējās pogas, M režīma
poga, ieslēgšanas slēdzis), kas veidota Android/iOS ierīcēm. USB ligzda ir tikai lādēšanai.

Uz Windows 11 tā strādā tikai uzreiz pēc pārošanas. Pēc pirmās atvienošanās (pults aizmieg
vai tiek izslēgta) tā vairs nav lietojama, kamēr lietotājs to ar roku neizdzēš no Bluetooth
ierīcēm un nesapāro no jauna.

R1 Keeper ir maza fona programma, kas šo izdzēšanu un pārošanu izdara pati, bez klikšķiem,
katru reizi, kad pults parādās.

## Ko mēs izmērījām (pamatojums)

Viss zemāk ir novērots uz šī datora (Windows 11 Insider, būvējums 26300.9974, Intel
Bluetooth) ar šo pulti (adrese `FF:FF:38:5D:40:01`, VID `0x248A`, PID `0x8266`).

1. **Kāpēc savienojums pārtrūkst.** Ja Windows ir saglabāta pārošana, tad pie katras
   atkalpieslēgšanās: Windows ieslēdz šifrēšanu ar saglabāto atslēgu (izdodas) → pults
   vēlreiz atsūta pārošanas atslēgas (SMP *Encryption Information*), lai gan pārošana
   nenotiek → Windows atbild *Pairing Failed* un ~0,1 s vēlāk atvienojas. Cikls atkārtojas
   ik pēc ~1,2 s bezgalīgi. Neviens pogu ziņojums šajā laikā neatnāk.
2. **Pēc svaigas pārošanas savienojums turas.** Novērots vairāk nekā 8 minūtes bez neviena
   atvienojuma; visi pults ziņojumi nonāk līdz Windows.
3. **Bez pārošanas nevar.** Pults ~1 s pēc pieslēgšanās pati pieprasa pārošanu (SMP
   *Security Request*); Windows uz to automātiski sāk pārošanu, un pults līdz tās beigām
   neatbild uz citiem pieprasījumiem. Tāpēc tieša piekļuve bez pārošanas atkrīt.
4. **Klusa automātiska pārošana strādā.** Ar WinRT API (`UnpairAsync`, pēc tam
   `Pairing.Custom.PairAsync` ar `ConfirmOnly`, ko programma pati apstiprina) pults tika
   sapārota bez lietotāja darbībām 6 reizes no 6. Katram mēģinājumam tika ņemts jauns
   `BluetoothLEDevice` objekts (`FromBluetoothAddressAsync`).
   - `UnpairAsync` aizņem ~1,2–2 s. Pati pārošana aizņem ~1,2–2,2 s.
   - **Ja saite pirms tam bija īsta** (pults pievienota vai tīri atvienota): noņemt
     pārošanu → sagaidīt pults sludinājumu, kas pienācis pēc `UnpairAsync` atgriešanās →
     pārot. Izdevās ar pirmo mēģinājumu 4 reizes no 4, ar nogaidīšanu 0 s, 2 s, 5 s un
     20 s pēc noņemšanas. Ar 0 s nogaidīšanu no noņemšanas līdz gatavai pārošanai
     pagāja 1,7 s.
   - **Ja pults tikko izslēgta un ieslēgta** (5. mērījums, Windows vēl tur "mirušu"
     saiti): pirmais pārošanas mēģinājums uzreiz pēc noņemšanas neizdevās 2 reizes no 2
     (`Failed` pēc 1,5–8 s); nākamais mēģinājums pie nākamā sludinājuma izdevās abas
     reizes. Kopā no pirmā sludinājuma līdz gatavai pārošanai: 8 s un 15 s.
5. **Windows atvienošanos pamana lēni.** Ja pulti izslēdz un pēc 5 s ieslēdz, Windows vēl
   uzskata, ka saite ir "Connected" (saites pārtrūkšanas taimauts ir 6 s), un
   `ConnectionStatusChanged` nepaspēj nostrādāt.
6. **Pults sludina arī tad, ja ir sapārota.** Sludinājuma tips `ConnectableUndirected`,
   nosaukums `Magicsee R1` ar atstarpēm galā.
7. **Režīmi.** Pēc ieslēgšanas pults vienmēr ir mūzikas režīmā: kloķis un A–D sūta
   multivides taustiņus (skaļāk, klusāk, nākamais, pauze), tikai priekšējās pogas ir
   kontroliera pogas. Pēc **M + B** (spēļu režīms) viss nāk kā kontroliera ziņojumi:

   | Pults | Windows kontrolierī |
   |---|---|
   | Kloķis augšup / lejup | Y ass 0 / 2 (miera stāvoklī 1) |
   | Kloķis pa kreisi / pa labi | X ass 0 / 2 (miera stāvoklī 1) |
   | A | pogas 5 un 10 reizē |
   | B | poga 1 |
   | C | pogas 4 un 9 reizē |
   | D | poga 2 |
   | Priekšējā augšējā | poga 8 |
   | Priekšējā apakšējā | poga 7 |

   Spēļu režīmu pults pēc izslēgšanas neatceras. No datora režīmu pārslēgt nevar.
8. **Kā Windows redz pulti.** Viena HID ierīce ar četrām kolekcijām: multivides taustiņi
   (lietojuma lapa `0x0C`, ziņojuma ID 2, 2 baiti; bits 0 = skaļāk, bits 1 = klusāk,
   bits 2 = pauze, bits 5 = nākamais), spēļu kontrolieris (ID 4, 2 baiti: 12 pogas + divas
   2 bitu asis), pele (ID 1), tastatūra (ID 3).

## Mērķi

1. Pēc pults ieslēgšanas vai pamošanās tā kļūst lietojama Windows bez lietotāja darbībām
   datorā. Mērķis: parasti līdz 5 s no brīža, kad pults sāk sludināt; sliktākajā gadījumā
   līdz 15 s.
2. Darbojas bez administratora tiesībām, bez draiveru instalēšanas, bez Windows testa režīma.
3. Ja pults ir mūzikas režīmā, lietotājs saņem atgādinājumu nospiest M + B.
4. Darbojas bez uzraudzības: startē kopā ar Windows, sēž paziņojumu joslā, raksta žurnālu.

## Kas nav mērķis

- Pogu pārkartēšana un virtuāls kontrolieris. A un C dubultpogas un ieslēgts/izslēgts
  kloķis paliek, kā ir. (Tas ir atliktais "ceļš 2", skat. zemāk.)
- USB.
- Vairākas pultis vienlaikus vai citi pulšu modeļi.
- Windows serviss (0. sesijā). Programma darbojas lietotāja sesijā.

## Ko programma nekad nedrīkst darīt

- Noņemt pārošanu, pārot vai kā citādi aiztikt jebkuru citu Bluetooth ierīci. Programma
  drīkst rīkoties tikai ar vienu ierīci — to, kuras adrese ir saglabāta kā programmas
  pults.
- Atvērt logus vai pārņemt fokusu (lietotājs var būt spēlē). Tikai paziņojumi, un tie
  nedrīkst atkārtoties bez gala.
- Bezgalīgi ātri mēģināt pārot/atpārot, ja kaut kas neizdodas. Jābūt pieaugošai pauzei.

## Ierobežojumi

- Windows 11, .NET 10 SDK (ir uzstādīts), mērķis `net10.0-windows10.0.19041.0`.
- WinRT API: `Windows.Devices.Bluetooth`, `Windows.Devices.Bluetooth.Advertisement`,
  `Windows.Devices.Enumeration`.
- Pārošanai jānotiek lietotāja interaktīvajā sesijā.
- Uzvedība ir pārbaudīta tikai uz Insider būvējuma 26300; uz citiem Windows būvējumiem tā
  var atšķirties.

## Apsvērtie ceļi

| Ceļš | Rezultāts |
|---|---|
| HID vai filtra draiveris | Atkrīt. Savienojumu pārtrauc Windows Bluetooth kodols pārošanas protokola līmenī, zem jebkura HID draivera. Kodola draiverim turklāt vajag Microsoft parakstu. |
| A — programma runā ar pulti tieši, bez pārošanas | Atkrīt (skat. 3. mērījumu). |
| **1 — savienojuma uzturētājs** | **Izvēlēts.** Lietotāja lēmums. |
| 2 — uzturētājs + tīrs virtuāls kontrolieris (ViGEmBus) | Atlikts. Vajadzīgs tikai tad, ja kāda programma prasa Xbox tipa kontrolieri vai traucē dubultpogas. ViGEmBus izstrāde ir apturēta. |

## Kā tas strādā

Viena .NET programma bez galvenā loga. Daļas:

### 1. Pults vērotājs

Klausās Bluetooth LE sludinājumus (aktīvā skenēšana).

Programma ir piesaistīta **vienai pultij pēc adreses**:

- Kamēr adrese nav saglabāta (pirmā palaišana vai pēc "Aizmirst pulti"), programma pieņem
  pirmo ierīci, kuras sludinājuma nosaukums sākas ar `Magicsee R1`, saglabā tās adresi
  iestatījumos un parāda paziņojumu ar šo adresi.
- Kad adrese ir saglabāta, programma reaģē tikai uz šo adresi. Citas ierīces ar tādu pašu
  nosaukumu tiek ignorētas.

Iemesls: pārošana tiek apstiprināta automātiski, un pults Windows acīs ir arī tastatūra un
pele. Ja pietiktu ar nosaukumu vien, jebkura tuvumā esoša ierīce ar šādu nosaukumu tiktu
klusi sapārota kā tastatūra.

Sludinājums no pults nozīmē, ka tā šobrīd **nav** strādājošā savienojumā (pievienota pults
nesludina). Tas ir galvenais signāls rīkoties.

### 2. Pārošanas pārvaldnieks

Vienlaikus notiek tikai viena darbība. Noteikumi:

- **Redzams sludinājums, Windows pulti uzskata par sapārotu** → noņemt pārošanu. Pārot pie
  pirmā sludinājuma, kas pienāk pēc tam, kad noņemšana ir pabeigta (papildu nogaidīšana
  nav vajadzīga — 4. mērījums).
- **Redzams sludinājums, pults nav sapārota** → pārot (`ConfirmOnly`, programma pati
  apstiprina), ar jaunu ierīces objektu. Ja izdodas — stāvoklis "pievienots".
- **Pārošana neizdodas** → mēģināt vēlreiz pie nākamā sludinājuma. Pirmie daži
  atkārtojumi notiek bez papildu pauzes, jo ātras izslēgšanas/ieslēgšanas gadījumā viena
  neveiksme ir sagaidāma (4. mērījums). Ja neveiksmes turpinās, pauze starp mēģinājumiem
  pieaug; pēc veiksmes tā atiestatās.
- **Saite pārtrūkst, kamēr pults ir sapārota** → uzreiz noņemt pārošanu. Tā nākamā
  pamošanās sākas no ātrā ceļa (uzreiz pārot, ~2 s), un Windows netērē laiku bezjēdzīgajam
  pieslēgšanās ciklam.
- **Programmas starts** → ja pults ir sapārota un pievienota, pieņemt to kā "pievienots".
  Ja pārošana patiesībā ir novecojusi, pults sludinās, un nostrādās pirmais noteikums.

Sagaidāmais laiks no pirmā sludinājuma līdz lietojamai pultij:

| Gadījums | Laiks |
|---|---|
| Pults bija prom pietiekami ilgi, pārošana jau noņemta | ~2 s |
| Pults sludina, pārošana vēl ir, saite bija īsta | ~4 s (noņemšana + pārošana) |
| Ātra izslēgšana/ieslēgšana (Windows tur "mirušu" saiti) | 8–15 s (izmērīts) |

### 3. Režīma atgādinājums

Kad pults ir pievienota, programma lasa tās multivides taustiņu kolekciju. Pirmais
ziņojums, kurā nospiests kāds taustiņš, nozīmē, ka pults ir mūzikas režīmā → paziņojums
"Pults ir mūzikas režīmā. Nospied M + B, lai pārslēgtu uz spēļu režīmu." Ne biežāk kā
vienu reizi katrā pieslēgšanās reizē.

Pats pirmais spiediens mūzikas režīmā skaļumu tomēr nomainīs — to programma novērst nevar.

### 4. Paziņojumu joslas ikona

- Ikona rāda stāvokli: pults nav redzama / notiek pārošana / pievienota.
- Izvēlne: stāvokļa rinda, "Pauzēt" / "Turpināt", "Aizmirst pulti", "Startēt kopā ar
  Windows" (ieslēdzams), "Atvērt žurnālu", "Iziet".
- "Pauzēt" ir domāts gadījumam, kad pulti grib lietot ar citu ierīci (piemēram,
  telefonu). Pauzējot programma noņem pults pārošanu no Windows, ja tāda ir, un pēc tam
  uz pults sludinājumiem nereaģē. Bez noņemšanas Windows pats turpinātu ik sekundi
  pieslēgties pultij un neļautu to lietot citur. "Turpināt" atjauno parasto darbību.
- "Aizmirst pulti" izdzēš saglabāto adresi (un noņem pārošanu, ja tāda ir); nākamā
  ieraudzītā `Magicsee R1` kļūst par programmas pulti.
- "Iziet" pārošanu neaiztiek: pults turpina strādāt līdz nākamajai atvienošanās reizei.
- Teksti latviešu valodā.

### 5. Pārējais

- Tikai viens programmas eksemplārs vienlaikus.
- Žurnāls `%LOCALAPPDATA%\R1Keeper\` — katra darbība ar laiku un rezultātu, ar izmēra
  ierobežojumu.
- Autostarts caur lietotāja `Run` reģistra atslēgu (bez administratora tiesībām).
- Iestatījumi (pults adrese, nosaukuma sākums, autostarts, pauzes stāvoklis) JSON failā
  tajā pašā mapē.
- Izmēģinājumu un mērījumu programmas bija vienreizējas un repozitorijā nav. Kods tiek
  rakstīts no jauna.

## Pārbaude

- Pārošanas pārvaldnieka loģika ir atdalīta no Bluetooth API tā, lai to var pārbaudīt ar
  automātiskiem testiem bez pults (viltus pults, viltus pārošana): visi noteikumi,
  atkārtojumi un pieaugošā pauze, "tikai viena darbība vienlaikus", piesaiste vienai
  adresei, "citas ierīces neaiztikt", pauze.
- Ar īsto pulti, ar roku:
  1. pults izslēgta ilgi → ieslēgt → lietojama, datorā neko nespiežot;
  2. ātra izslēgšana/ieslēgšana (5 s);
  3. pults pati aizmieg un tiek pamodināta ar pogu;
  4. programmas starts, kad pults jau ir pievienota;
  5. pauze: pults pārošana no Windows ir noņemta, un programma to neatjauno, kamēr nav
     nospiests "Turpināt";
  6. mūzikas režīmā parādās atgādinājums, pēc M + B kontrolieris strādā;
  7. desmit ieslēgšanas reizes pēc kārtas bez ejas uz Bluetooth iestatījumiem.

## Zināmie riski un nepārbaudītais

- **Pults pašas aizmigšana nav pārbaudīta** — izmēģinājumā pults tika tikai izslēgta ar
  slēdzi. Dizains abus gadījumus apstrādā vienādi, bet tas jāpārbauda (3. pārbaude). Nav
  zināms arī, pēc cik ilga laika pults aizmieg (8 minūtēs neaizmiga) un vai pēc pamošanās
  tā paliek spēļu režīmā.
- Automātiskā pārošana izmēģināta sešas reizes vienā vakarā, visas uz viena datora.
- Starp pārošanas noņemšanu un jauno pārošanu Windows var parādīt savu paziņojumu "Tap to
  set up your Magicsee R1". Ja tas traucē, jāskatās, vai to var novērst.
- Katra pārošana rada Windows ierīces pievienošanas/atvienošanas skaņas.

## Būvēšanas laikā atklātais (papildināts pēc pārbaudes ar pulti)

- **Sludinājums nenozīmē, ka pults nav savienojumā.** Apmēram 60 s pēc katras pārošanas
  Windows piegādā pults sludinājumu, lai gan saite ir dzīva. Sākotnējais noteikums
  "sludinājums + sapārota → noņemt pārošanu" tāpēc reizi minūtē lieki pāroja veselu pulti.
  Noteikums ir mainīts: pārošanu noņem tikai tad, ja pults sludina **un saite nav
  pievienota**. Sludinājumi, kas vecāki par 3 s, tiek atmesti.
- **Pārošana, kas izdodas, bet neturas.** Pārošana par veiksmi skaitās tikai tad, ja tā
  noturas 30 s; citādi tā skaitās neveiksme un stājas spēkā pieaugošā pauze.
- **Bluetooth izsaukumiem ir 60 s noilgums**, lai iestrēdzis izsaukums neapturētu programmu.
- Pults aizmieg pēc ~5 min bez pogu spiešanas un pēc pamošanās atkal ir mūzikas režīmā.
  Pamošanās līdz lietojamai pultij: 1,7 s.
- Pārbaudes sesijā: 18 veiksmīgas pārošanas; 4 neveiksmīgi mēģinājumi, visi ātras
  izslēgšanas/ieslēgšanas gadījumā un katram sekoja veiksmīgs.

## Mani pieņēmumi (nevis lietotāja lēmumi)

- Nosaukums "R1 Keeper".
- C#, .NET 10, paziņojumu joslas programma lietotāja sesijā, nevis Windows serviss.
- Programma piesaistās vienai pultij pēc adreses; adresi tā iemācās no pirmās ieraudzītās
  `Magicsee R1`, un to var atiestatīt ar "Aizmirst pulti".
- "Pauzēt" noņem pults pārošanu no Windows; "Iziet" to neaiztiek.
- Saskarnes teksti latviski.
- Pārošanas noņemšana uzreiz pēc saites pārtrūkšanas.
- Projekta mapē tiek izveidots git repozitorijs.

## Atstāts būvētājam

Projekta struktūra, precīzi pauzes ilgumi, ikonas, kā tieši lasīt multivides kolekciju
(HID API vai Raw Input), paziņojumu veids (joslas balons vai Windows paziņojums), žurnāla
formāts.
