# Empfänger-ID für private Nachrichten

Storage speichert `SenderId` als `sender_id`, `TargetId` als `receiver_id` und den durch
`get_or_create_private_room` bereitgestellten privaten Raum weiterhin als `room_id`.
Das Event an `delivery_queue` bleibt unverändert; es enthält weiterhin `TargetId`.

## Reihenfolge der Einführung

1. Schemaänderung mit dem Team prüfen und ein Backup gemäß eurem üblichen Verfahren sicherstellen.
2. [Migration Phase 1](Migrations/20260905_add_receiver_id.sql) im vorgesehenen Supabase-Projekt anwenden.
3. Prüfen, ob `receiver_id` über die Data API verfügbar ist. Die Migration fordert das
   Neuladen des PostgREST-Schema-Caches an.
4. Danach den neuen Storage-Service bauen und veröffentlichen.
5. [Migration Phase 2](Migrations/20260906_require_receiver_id_for_new_messages.sql) ausführen,
   damit neue/geänderte Nachrichten nicht mehr ohne Empfänger gespeichert werden können.
6. [Migration für automatische Privaträume](Migrations/20260911_get_or_create_private_room.sql)
   anwenden. Sie erstellt keine Räume vorab, sondern stellt die Funktion für Storage bereit.
7. Den vollständigen Anwendungstest starten.

Die Migration wird **nicht automatisch** beim Starten des Services oder der Tests ausgeführt.
Ohne neue Spalte würde das Speichern fehlschlagen. Der Anwendungstest prüft deshalb vor
dem Nachrichtenversand, ob die Spalte abfragbar ist.

Die Migration zunächst ohne Supabase in einer neuen Wegwerf-Datenbank prüfen (Docker Desktop muss laufen):

```powershell
.\Database\Tests\Run-MigrationTest.ps1
```

Der Helfer erstellt nur einen temporären PostgreSQL-Container, führt
[receiver_id_migration_test.sql](Tests/receiver_id_migration_test.sql) aus und entfernt den
Container anschließend. Er prüft Wiederholbarkeit, Altbestand, Pflichtfeld, Fremdschlüssel,
Indizes, unveränderten RLS-Status und den History-Filter in beiden Richtungen. Zusätzlich
prüft er, dass ein vorhandener Privatraum wiederverwendet und beim ersten Kontakt genau
ein neuer Privatraum mit zwei Mitgliedern erstellt wird. Zwei parallele erste Nachrichten
werden ebenfalls ausgeführt; danach darf für das Benutzerpaar nur ein Raum existieren.

## Automatischer privater Raum

Storage führt vor dem Speichern genau einen RPC-Aufruf aus. Die Datenbankfunktion sucht
und erstellt innerhalb derselben Transaktion. A → B und B → A verwenden denselben Lock
und damit denselben Raum. Das verhindert doppelte Räume bei gleichzeitig eintreffenden
ersten Nachrichten. Nur der Backend-Rolle `service_role` ist der Funktionsaufruf erlaubt.

Der Service darf erst mit diesem Code gestartet werden, nachdem die RPC-Migration im
Zielprojekt angewendet wurde. Andernfalls schlägt `rpc/get_or_create_private_room` fehl
und MassTransit wiederholt die Nachricht; sie wird nicht an `delivery_queue` gesendet.

## Was die Migration ändert

- Neue nullable UUID-Spalte `messages.receiver_id`, ohne zufälligen Standardwert.
- Phase 2 ergänzt einen zunächst nicht gegen Altbestände validierten Check. Er verhindert bei
  neuen oder geänderten Nachrichten eine leere `receiver_id`, darf aber erst nach dem
  Storage-Deployment aktiviert werden.
- Fremdschlüssel zu `public.profiles(id)`, entsprechend dem bestehenden Ziel von `sender_id`.
  Kein automatisches Löschen von Nachrichten; die neue Referenz kann das Löschen eines
  noch referenzierten Profils verhindern und muss mit eurem Löschkonzept abgestimmt werden.
- Zwei Indizes über Raum, Sender/Empfänger, Zeitstempel und Nachrichten-ID für beide
  Richtungen privater History-Abfragen.
- Keine neuen RLS-Regeln, keine geänderten Berechtigungen und keine gelöschten Nachrichten.

Die Spalte bleibt im Tabellenschema zunächst nullable: Altbestände und vorhandene
Gruppen-Nachrichten dürfen nicht mit geratenen Empfänger-IDs versehen werden. Neuer privater
Storage-Code setzt sie immer; Phase 2 erzwingt das für neue/geänderte Zeilen.
Vorhandene gleichnamige Spalten/Constraints/Indizes vorab auf passende Definitionen prüfen.

## Vorhandene Nachrichten

Die Migration ergänzt bewusst keine Empfänger bei alten Nachrichten. Nutzt dafür bei Bedarf
den [separaten Backfill-Probelauf](Backfill/review_receiver_ids.sql):

- Nur manuell geprüfte Räume in die Freigabeliste eintragen.
- Der Raum muss privat sein, genau zwei Mitglieder haben und den Sender enthalten.
- Es werden nur fehlende Empfänger ergänzt; bereits gesetzte Werte bleiben unverändert.
- Aktuelle Mitgliedschaft reicht nicht aus, falls Mitglieder früher gewechselt haben.
- Standardmäßig ist die Liste leer und der Probelauf endet mit `ROLLBACK`.
  Erst nach Prüfung und Teamfreigabe bewusst mit `COMMIT` anwenden.

Ungeklärte Altbestände bleiben `NULL`. Eine History, die nur über `receiver_id` filtert,
würde diese alten Nachrichten weiterhin nicht vollständig finden.

## History in beide Richtungen (Aufgabe des History-Endpunkts)

Für einen privaten Chat müssen beide Richtungen berücksichtigt werden:

```sql
WHERE room_id = @room_id
  AND ((sender_id = @current_user AND receiver_id = @other_user)
    OR (sender_id = @other_user AND receiver_id = @current_user))
ORDER BY created_at, id
```

Das ist nur die Filterbedingung, kein neuer Endpoint. `@current_user` muss aus der verifizierten
Anmeldung kommen; Raum-Mitgliedschaft und RLS müssen den Zugriff absichern. Nicht einfach
einen frei angegebenen Benutzer oder einen Backend-Secret-Key als Ersatz für Benutzerrechte verwenden.

Die Storage-Tests prüfen das Speichern beider Richtungen und die richtige Empfänger-ID.
Ein vollständiger History-Test muss zusätzlich mit den Benutzer-Sitzungen von A und B
laden und einen unbeteiligten Benutzer ausschließen. Das ist nicht durch einen Admin-Read bewiesen.

Technische Referenzen: [PostgreSQL ALTER TABLE](https://www.postgresql.org/docs/current/sql-altertable.html),
[CREATE INDEX](https://www.postgresql.org/docs/current/sql-createindex.html),
[PostgREST Schema-Cache](https://postgrest.org/en/stable/references/schema_cache.html).
