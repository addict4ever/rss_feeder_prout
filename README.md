# rss_feeder_prout
Lecteur RSS moderne sous .NET MAUI 9.0 offrant une expérience Offline-First. Gérez vos playlists, synchronisez vos flux favoris et téléchargez le contenu complet pour une lecture hors ligne sans distraction. Inclut un système d'archivage sécurisé, un moteur de recherche performant et des outils avancés de maintenance SQLite.

# 🚀 RSS Feeder Prout

**RSS Feeder Prout** est une application de lecture de flux RSS moderne et robuste développée avec **.NET MAUI 9.0**. Conçue pour offrir une expérience de lecture fluide, rapide et totalement **Offline-First**, elle vous permet de rester informé sans dépendre d'une connexion internet permanente.

![.NET MAUI 9.0](https://img.shields.io/badge/.NET%20MAUI-9.0-purple.svg)
![SQLite](https://img.shields.io/badge/Database-SQLite-blue.svg)
![Architecture](https://img.shields.io/badge/Architecture-MVVM-orange.svg)
![Platform](https://img.shields.io/badge/Platform-Android%20%7C%20Windows-green.svg)

---

## ✨ Fonctionnalités Clés

### 📂 Organisation par Playlists
* **Gestion Thématique :** Créez des playlists (ex: *Tech*, *Cuisine*, *Actualités*) pour regrouper vos sources d'information.
* **Filtres Intelligents :** Un moteur de recherche dynamique intégré qui ignore les accents (ex: rechercher "cinema" trouvera "Cinéma") pour une réactivité instantanée.
* **Édition Intuitive :** Ajoutez, modifiez ou supprimez vos flux RSS (sites) directement au sein de vos playlists.

### 📖 Expérience de Lecture Premium
* **Mode Hors Ligne (Offline) :** Téléchargez le contenu complet des articles. L'application extrait le texte et les images pour une consultation parfaite, même sans réseau.
* **Extraction Intelligente :** Nettoyage automatique du HTML superflu via **AngleSharp** pour ne garder que l'essentiel du texte.
* **États de Lecture :** Suivi visuel clair entre les articles lus, non lus et ceux disponibles hors ligne.

### 📦 Archivage & Sauvegarde
* **Coffre-fort d'articles :** Sauvegardez vos articles favoris dans la section **Archives**. Ils resteront protégés même lors du nettoyage du cache global.
* **Persistance Totale :** Vos archives sont stockées localement dans une table dédiée pour une sécurité maximale de vos données.

### 🛠 Maintenance & Administration (Avancé)
* **Nettoyage Chirurgical :** Gérez votre espace de stockage grâce à des options de suppression précises :
    * **Par ancienneté :** Supprimez les articles de plus de 1, 2, 3 ou 7 jours, ou même par mois (1 à 3 mois).
    * **Par état :** Supprimez uniquement les articles déjà lus.
    * **Archives :** Option pour purger les archives de plus de 6 mois.
* **Exportation de la DB :** Exportez votre fichier `RssProutDB.db3` en un clic pour une sauvegarde externe ou une analyse.
* **Indicateurs Système :** Surveillance en temps réel de l'état de la base de données et indicateurs d'activité (IsBusy).

---

## 🏗 Stack Technique

L'application repose sur une architecture **MVVM** (Model-View-ViewModel) garantissant une séparation stricte entre la logique métier et l'interface utilisateur.

| Technologie | Usage |
| :--- | :--- |
| **.NET MAUI 9.0** | Framework multiplateforme natif. |
| **SQLite-net-pcl** | Moteur de base de données locale haute performance. |
| **CodeHollow.FeedReader** | Parsing robuste des flux RSS et Atom. |
| **AngleSharp** | Analyse et nettoyage intelligent du contenu HTML. |
| **CommunityToolkit.Mvvm** | Standard de l'industrie pour la gestion des commandes et notifications. |

---

## 🚀 Installation

1.  **Prérequis :** Visual Studio 2022 avec la charge de travail ".NET MAUI" installée.
2.  **Clonage du dépôt :**
    ```bash
    git clone [https://github.com/addict4ever/rss-feeder-prout.git](https://github.com/addict4ever/rss-feeder-prout.git)
    ```
3.  **Configuration :** Restaurez les packages NuGet via Visual Studio.
4.  **Déploiement :** Sélectionnez le projet `Rss_feeder_prout` et lancez-le sur votre appareil Android ou Windows.

---

## 📁 Structure du Projet

* **/Models :** Définition des schémas SQLite (Playlists, Sites, Articles, Archives).
* **/ViewModels :** Logique de navigation, filtrage de recherche et commandes de maintenance.
* **/Services :**
    * `SQLiteService` : Gestion asynchrone des données locales.
    * `RssService` : Moteur de synchronisation et de téléchargement de contenu.
* **/Views :** Interfaces XAML optimisées pour le mode sombre.

---

## 📄 Licence
Ce projet est développé à des fins personnelles et éducatives.

---

> **Note de l'auteur :** > *"Développé avec la conviction que vos données et vos lectures vous appartiennent. Pas d'algorithmes, pas de cloud obligatoire, juste l'information brute sous votre contrôle total."* 🛠️💡
