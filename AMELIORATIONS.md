# Rss Prout 2.0 — ce qui a changé

## 1. Corrections importantes

| Problème | Cause | Correction |
|---|---|---|
| La mise à jour automatique ne reprenait pas aux heures prévues | Minuteur qui repartait de zéro à chaque lancement, arrêté quand l'app est fermée, aucun rattrapage | `Services/SyncScheduler.cs` : l'échéance est **calculée et enregistrée**, une échéance manquée est **rattrapée** au lancement ou au retour dans l'app, et sur Android une **tâche système (JobScheduler)** lance la synchro **même app fermée** (elle survit au redémarrage). Deux modes : **intervalle** ou **heures fixes** (ex. 07:00, 12:00, 18:00). |
| Mauvais article ouvert / « lu » non enregistré pour les anciens articles | Une base par jour : les Id se répétaient d'un fichier à l'autre, et les mises à jour visaient la base du jour seulement | **Une seule base d'articles** avec un index unique (source + GUID). Migration automatique des anciennes bases. |
| Cache d'images inutile, puis images cassées hors-ligne | `GetHashCode()` change à chaque lancement en .NET, et les images en cache étaient réécrites vers le proxy Google APRÈS la mise en cache | Nom de fichier = SHA1 stable ; le proxy Google n'est plus qu'un **secours optionnel** ; les images locales sont intégrées au lecteur (data URI). |
| Boutons de nettoyage sans effet | Ils appelaient `CleanDatabaseCommand`, qui n'existait pas | Commande ajoutée (1 j, 3 j, 7 j, 1 mois, 3 mois, articles lus, archives + 6 mois). |
| Sauvegarde parfois incomplète | Copie des .db3 en mode WAL sans « checkpoint » | `VACUUM INTO` : copie cohérente **et compactée**. |
| La synchro bloquait toute l'application | Écran de chargement plein écran | Synchro **en arrière-plan** avec un bandeau de progression : vous continuez à lire pendant la synchro. |
| Le bouton « Sync » des playlists ne faisait rien | Affichait seulement un message | Vraie synchronisation. |
| Le lecteur n'affichait pas le HTML préparé, et le bouton retour était cassé | Mauvaise liaison XAML, commande inexistante | Lecteur entièrement refait. |
| Mise à jour d'archives sur la mauvaise base | `CleanupArchivesAsync` visait la base du jour | Corrigé. |
| `CustomCleanSelectors` utilisé à la fois comme zone de contenu ET comme éléments à retirer | Deux usages contradictoires | Deux champs séparés : **Sélecteur du contenu** et **Éléments à retirer**, modifiables dans l'app. |
| Accents cassés sur certains sites | Seul l'encodage ISO-8859 était géré | Tous les encodages (windows-1252…), avec lecture de `<meta charset>`. |
| Flux http:// refusés | Android bloque le « cleartext » | `usesCleartextTraffic="true"`. |
| Icônes dupliquées à chaque synchro | Nom de fichier avec un Guid aléatoire | Nom stable par site. |
| « Lire la suite » pouvait remplacer l'article par une autre page, avec 2 à 4,5 s d'attente par article | Détection beaucoup trop large (« plus », « post », « nav »…) | Seuls de vrais liens « lire la suite » du même site sont suivis, et seulement si la page ressemble à un extrait (300 à 900 ms d'attente). |
| SmartReader ne fonctionnait jamais dans le contournement structurel | Le HTML lui était passé comme si c'était une URL | Corrigé (`new Reader(url, html)`). |
| `AutoAcceptCookies` plantait | `:contains()` n'est pas du CSS valide | Recherche par texte, et la méthode est maintenant réellement utilisée. |

## 2. Nouvelles fonctionnalités (25)

1. **Articles « en ligne »** : les articles du flux qui ne sont pas encore dans la base s'affichent quand même (badge bleu). Vous pouvez les lire, puis les enregistrer un par un ou tous d'un coup.
2. **Trois états bien visibles** sur chaque article (bande de couleur + pastille) : 🔵 **En ligne** · 🟡 **Résumé enregistré** · 🟢 **Hors-ligne** (et « + images »).
3. **Choix avant chaque synchro** : flux seulement (rapide) ou tout télécharger, puis images **aucune / optimisées (redimensionnées + compressées) / originales**. Vous pouvez retenir votre choix.
4. **Images optimisées** : largeur maximale et qualité JPEG réglables (jusqu'à 10× plus léger).
5. **Lecture pendant la synchro** : bandeau non bloquant, avec bouton Arrêter.
6. **Article complet à la volée** : un article pas encore téléchargé est récupéré en ligne à l'ouverture, sans être enregistré, puis vous pouvez le garder d'une touche.
7. **Favoris** (onglet dédié).
8. **À lire plus tard**.
9. **Recherche plein texte** dans tous les articles (titre, résumé, contenu téléchargé, source, auteur). Elle ignore les accents et les majuscules, et propose des filtres de période, de non-lus et de hors-ligne, ainsi que l'historique des recherches.
10. **Flux intelligents** : Non lus, Aujourd'hui, Tous, Favoris, Plus tard, Hors-ligne.
11. **Compteurs de non-lus** par catégorie et par source.
12. **Lecteur personnalisable** : taille du texte, thème (comme l'app, sombre, clair, sépia, noir OLED), police avec ou sans empattement, interligne, texte justifié.
13. **Lecture à voix haute** (synthèse vocale en français).
14. **Article précédent / suivant / prochain non lu** directement depuis le lecteur.
15. **Gestes** : glisser à droite = lu / non lu, glisser à gauche = favori, plus un menu complet par article.
16. **Import / export OPML** (le service existait mais n'était branché nulle part).
17. **Découverte automatique des flux** : collez l'adresse d'un site (ex. `korben.info`) et le flux RSS / Atom est trouvé tout seul.
18. **Santé des flux** : état, dernière réussite, erreur exacte, « Tester le flux », « Tester tous les flux ».
19. **Requêtes conditionnelles (ETag / Last-Modified)** : un flux qui n'a pas changé n'est pas retéléchargé, ce qui économise les données.
20. **Synchro Wi-Fi uniquement** ou **seulement en charge**, plus une **notification** en fin de synchro automatique.
21. **Nettoyage automatique** : articles lus après N jours, tous les articles après N jours. Favoris, « plus tard » et archives sont toujours conservés.
22. **Filtres par mots-clés** : masquer des sujets, et surligner ou signaler des mots surveillés.
23. **Statistiques** : chiffres clés, articles par jour, sources les plus actives, sources en erreur, **journal des synchros**.
24. **Thème clair / sombre / système** pour toute l'application.
25. **Gestion des sources** : renommer, changer l'URL, déplacer vers une autre catégorie, mettre en pause, « toujours télécharger complet », « ouvrir dans le navigateur », couleur et ordre des catégories. **Notes personnelles** sur les archives, et **page web d'origine dans l'app**.

## 3. Bases de données réorganisées

```
AppData/
  Databases/RssConfig.db3     catégories, sources, journal de synchro
  Databases/RssArticles.db3   TOUS les articles (une seule base)
  Databases/RssArchives.db3   archives permanentes
  ImageCache/                 images hors-ligne
  Icons/                      icônes des sites
  Backups/                    sauvegardes .zip
  ColdArchives/               anciens articles rangés par mois (.db3 ou .zip)
  Legacy/                     vos anciennes bases RssProut_AAAA_MM_JJ.db3 (après migration)
```

**Migration automatique au premier lancement** : vos sources, catégories, archives et tous vos anciens articles (fichiers quotidiens) sont repris. Les anciens fichiers sont rangés dans `Legacy/` et non supprimés ; vous pourrez les supprimer depuis *Données & sauvegardes*.

Nouveaux champs : vraie date de publication (tri correct), date de lecture, favori, plus tard, images en cache, temps de lecture, nom du site enregistré (avant il était perdu), texte de recherche normalisé, état de santé des sources.

## 4. Sauvegarde, restauration, compression, archives

*Plus → Données & sauvegardes* :

- **Espace utilisé** détaillé (bases, images, sauvegardes, archives par mois).
- **Créer une sauvegarde** (.zip) : articles oui ou non, images oui ou non, compression rapide / équilibrée / maximale, réglages inclus.
- **Sauvegarde automatique hebdomadaire** (les 5 dernières sont gardées).
- **Restaurer** en remplaçant, ou en **fusionnant** les articles. Une sauvegarde de sécurité est créée automatiquement avant.
- **Importer** : sauvegarde .zip (y compris l'**ancien format** `Backup_Total_*.zip`), base .db3 (détection automatique de la configuration, des articles ou des archives, et **fusion sans doublons**) et fichier .opml.
- **Exporter** : sauvegarde complète, OPML, articles en JSON, chaque base séparément. **Favoris et archives** en page HTML lisible partout.
- **Compacter les bases** (VACUUM + ANALYZE), avec l'espace gagné affiché.
- **Vérifier l'intégrité**.
- **Archiver les anciens articles par mois** dans des fichiers séparés, compressés en .zip au besoin, et **réintégrables** à tout moment.
- **Vider le cache d'images** (tout, ou les images de plus de 7 ou 30 jours) et **supprimer les images inutilisées**.

## 5. Design et navigation

- **Onglets en bas** : Accueil · Recherche · Favoris · Archives · Plus. Bouton retour Android logique : page précédente, puis onglet Accueil, puis double appui pour quitter.
- Accueil : salutation, état de la synchro (dernière et prochaine), tuiles des flux intelligents, catégories en couleur avec compteurs.
- Icônes vectorielles maison (`Helpers/IconPaths.cs`, `Controls/IconView.cs`), sans police d'icônes à installer. L'ancien code faisait référence à « FAS » (Font Awesome), qui n'était pas installée.
- Palette sombre ou claire (`Services/ThemeService.cs`), typographie cohérente, cartes arrondies, pastilles d'état.
- Gestion de l'affichage bord à bord d'Android 15 (marge sous la barre d'état).
- Votre mascotte smiley est conservée dans l'onglet *Plus* (touchez-la 😉).

## 6. Méthodes conservées

Toutes vos méthodes d'origine sont gardées et rendues fonctionnelles :

- **Anti-blocage / paywall** (`Services/Rss/RssService.Extraction.cs`) : `CreateStealthHttpClient`, `HandleDeepNavigationAsync`, `AutoAcceptCookies`, `ApplyStructuralBypass`, `ExtractMainContentAsync`, `PreParseSanitize`, `FetchRawContentAsync`, `FinalSanitizeAndOptimizeAsync`, `EnsureUtf8Encoding`, `FixRelativeUrls`, `PreCleanHtml`. Le client « furtif » sert aussi maintenant de seconde tentative quand un flux répond 403 / 429 / 503.
- **Images** : `DownloadImageToLocalAsync`, `CacheHtmlImagesLocallyAsync`, `ProcessAndCacheImagesAsync`, `ExtractImageUrlFromHtml`, `DownloadAndSaveIconAsync`, proxy Google (en option).
- **Base** : `CreateDefaultData` (votre liste de flux par défaut, telle quelle), `IsArticleAlreadyDownloadedAsync`, `ItemExistsInAnyDatabaseAsync`, `GetAllArticlesFromAllDatabasesAsync`, `CleanHtmlContent`, `CleanAllContentInDatabaseAsync`, `CleanupByTimeAsync`, etc.
- **Synchro** : `SynchronizeSitesInPlaylistAsync`, `PerformFeedUpdateAsync`, `DownloadAllContentForPlaylistAsync`, `EnqueueDownload`, `UpdateAndGetFeedItemsForSitesAsync`, `ExecuteFullGlobalSyncCommand`, `StopSync`.
- **Lecteur** : le nettoyage ZATAZ / partage / newsletter est conservé, maintenant avec AngleSharp (les regex cassaient les balises imbriquées). L'inspecteur de code source est conservé (le bouton « Fermer » revient maintenant à l'article).
- **Flux** : CodeHollow.FeedReader reste en secours ; un analyseur maison gère en priorité RSS 2.0, RSS 1.0/RDF, Atom et **JSON Feed**, ainsi que les médias (`media:content`, `media:thumbnail`, enclosures, `itunes:image`), les dates exotiques (fuseaux EST/EDT…, dates en français) et les flux XML mal formés.

## 7. Structure du code

```
Controls/      IconView, IconButton, ChipView, MenuRowView, ArticleCardView, SyncBannerView
Helpers/       HtmlHelper, DateHelper, FormatHelper, IconPaths, ReaderHtmlBuilder
Models/        RssItem, FeedSite, FeedPlaylist, ArchiveItem, AppModels (requêtes, synchro, stockage)
Services/      AppSettings, ThemeService, SyncService, SyncScheduler, BackupService,
               NotificationService, ReaderSession, OpmlService, UiHelper
Services/Database/  SQLiteService (.cs, .Config, .Articles, .Archives, .Maintenance)
Services/Rss/       RssService (.cs, .Extraction, .Images), FeedParser, ImageCacheService
Platforms/Android/  AndroidSyncJobScheduler, RssSyncJobService (synchro app fermée)
ViewModels/    une classe par écran + ArticleListViewModelBase commune
Views/         Accueil, Sources, Liste d'articles, Lecteur, Recherche, Favoris, Archives, Plus,
               Catégories, Sources d'une catégorie, Données & sauvegardes, Réglages,
               Statistiques, Code source, Choix de synchro
```

## 8. À savoir

- Aucun nouveau paquet NuGet : tout repose sur ceux que vous aviez déjà, plus les API intégrées à Android et à MAUI.
- Au premier lancement, la migration peut prendre quelques secondes si vous aviez beaucoup de bases quotidiennes.
- Android peut décaler une synchro en arrière-plan de quelques minutes pour économiser la batterie (c'est normal). L'option « 5 minutes » ne fonctionne que lorsque l'app est ouverte : le minimum d'Android est de 15 minutes.
- Pour les notifications sur Android 13+, l'autorisation est demandée quand vous activez « Notification à la fin ».

## 9. Correctif 2.0.1 — plantages à l'ouverture des articles

- **Images du lecteur** : elles étaient converties en texte base64 (jusqu'à 30 Mo envoyés au WebView), ce qui faisait manquer de mémoire au WebView. Elles sont maintenant lues directement depuis les fichiers (`file://`), et un article énorme est tronqué proprement.
- **Extraction de l'article complet (méthodes paywall comprises)** : elle tournait sur le thread de l'interface, ce qui figeait l'app jusqu'à ce qu'Android la ferme. Elle tourne maintenant entièrement en arrière-plan.
- **SmartReader et le contournement structurel** tournent sur un thread à grande pile. Une page très imbriquée provoquait un dépassement de pile, impossible à intercepter.
- **Toute erreur pendant l'extraction** est interceptée : l'article reste lisible en résumé au lieu de fermer l'app.
- **Mises à jour des articles depuis les téléchargements en arrière-plan** : elles sont toujours renvoyées sur le thread UI (Android plante si l'interface est modifiée depuis un autre thread).
- **Nouveau journal des erreurs** : *Plus → Journal des erreurs* (voir, partager, effacer). Les erreurs non gérées sont journalisées au lieu de fermer l'application.

## 10. Méthodes anti-paywall désactivées (2.0.2)

Elles sont **désactivées par défaut**. Le code reste dans `RssService.Extraction.cs`, et un interrupteur permet de les réactiver : *Réglages → Synchro manuelle & images → Méthodes anti-paywall*.

Quand elles sont désactivées :
- Les pages sont téléchargées avec un client HTTP normal : identité honnête « RssProut », pas de faux navigateur, pas de fausse IP `X-Forwarded-For`.
- Pas de navigation profonde « Lire la suite », pas de contournement structurel (flou, contenu masqué), pas de retrait des bannières de cookies.
- Pas de seconde tentative « furtive » quand un flux répond 403, 429 ou 503.
- Pas de proxy d'images Google.

L'extraction normale (SmartReader + analyse de densité + sélecteurs du site) continue de fonctionner. Pour un article réservé aux abonnés, seule la partie publique est affichée ; ouvrez-le dans le navigateur pour le reste.
