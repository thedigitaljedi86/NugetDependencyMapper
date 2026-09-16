# NuGet Map — projektbeskrivelse

Målet er et udviklerværktøj, der kan køres som `dotnet nuget-map` og omsætte NuGet-afhængigheder i en solution, et projekt eller en mappe til én flot, interaktiv HTML-rapport.

## Første implementerede version

- Klik på et projekt for at se dets direkte og transitive pakker.
- Klik på en pakke for at se berørte projekter, versioner og frameworks.
- Advarsler om pakker med forskellige gendannede versioner på tværs af projekter og targets.
- Navigerbar dependency graph med zoom, panorering og dybdevalg.
- Afhængighedskæder, der forklarer, hvorfor en pakke indgår.
- Søgbar pakkeoversigt, JSON-eksport og exitkoder til CI.
- Selvstændig HTML uden eksterne scripts, server eller internetkrav.
- Tydelig markering af manglende restore-data og illustrative demo-data.
- Analysebemærkninger har fejl- og advarselsniveau; kun fejl og manglende restore-data udløser `--fail-on-incomplete`.
- Licensmetadata pr. pakkeversion med licensudtryk, licensfil, licenslink, ukendt status og krav om licensaccept samt filtrering i pakkeoversigten.
- Rekursiv scanning af flere repositories i én rapport; projekter identificeres via deres stier.
- Footer med “Powered by IT Performance” og link til https://itperformance.dk.
- Brugeren bliver spurgt om rapportens navn ved interaktiv kørsel; `--name` angiver titlen direkte i automatiserede kørsler.
- Rapportens top viser den valgte titel og rapportmetadata uden marketingintroen.

Koden ligger i `src/NugetDependencyMapper`, testprogrammet i `tests/NugetDependencyMapper.Tests`, og HTML/CSS/JavaScript er indlejrede ressourcer i CLI-pakken.

## Mulige næste skridt

Sårbarhedsoplysninger, baseline-diffs mellem builds, opgraderingsplaner for transitive pakker, fælles versionspolitik og team-ejerskab. Funktionerne er beskrevet nærmere i [README.md](README.md), som også indeholder installation, kørselseksempler og kendte afgrænsninger.
