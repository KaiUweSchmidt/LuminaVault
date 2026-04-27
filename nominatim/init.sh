#!/bin/bash -ex

# Phasenbasiertes Init-Skript als Ersatz fuer /app/init.sh
# Schreibt nach jeder Phase einen Marker, damit bei Crash
# nicht alles neu gestartet werden muss.

MARKER_DIR="/nominatim/import-progress"
mkdir -p "$MARKER_DIR"

OSMFILE=/nominatim/data.osm.pbf
CURL=("curl" "-L" "-A" "${USER_AGENT}" "--fail-with-body")
SCP='sshpass -p DMg5bmLPY7npHL2Q scp -o StrictHostKeyChecking=no u355874-sub1@u355874-sub1.your-storagebox.de'

if [ -z "$THREADS" ]; then
  THREADS=$(nproc)
fi

# ===== Optionale Daten (Wikipedia, Postcodes, Tiger) =====
if [ "$IMPORT_WIKIPEDIA" = "true" ]; then
  ${SCP}:wikimedia-importance.sql.gz ${PROJECT_DIR}/wikimedia-importance.sql.gz
elif [ -f "$IMPORT_WIKIPEDIA" ]; then
  ln -s "$IMPORT_WIKIPEDIA" ${PROJECT_DIR}/wikimedia-importance.sql.gz
else
  echo "Skipping optional Wikipedia importance import"
fi

if [ "$IMPORT_GB_POSTCODES" = "true" ]; then
  ${SCP}:gb_postcodes.csv.gz ${PROJECT_DIR}/gb_postcodes.csv.gz
elif [ -f "$IMPORT_GB_POSTCODES" ]; then
  ln -s "$IMPORT_GB_POSTCODES" ${PROJECT_DIR}/gb_postcodes.csv.gz
else
  echo "Skipping optional GB postcode import"
fi

if [ "$IMPORT_US_POSTCODES" = "true" ]; then
  ${SCP}:us_postcodes.csv.gz ${PROJECT_DIR}/us_postcodes.csv.gz
elif [ -f "$IMPORT_US_POSTCODES" ]; then
  ln -s "$IMPORT_US_POSTCODES" ${PROJECT_DIR}/us_postcodes.csv.gz
else
  echo "Skipping optional US postcode import"
fi

if [ "$IMPORT_TIGER_ADDRESSES" = "true" ]; then
  ${SCP}:tiger2023-nominatim-preprocessed.csv.tar.gz ${PROJECT_DIR}/tiger-nominatim-preprocessed.csv.tar.gz
elif [ -f "$IMPORT_TIGER_ADDRESSES" ]; then
  ln -s "$IMPORT_TIGER_ADDRESSES" ${PROJECT_DIR}/tiger-nominatim-preprocessed.csv.tar.gz
else
  echo "Skipping optional Tiger addresses import"
fi

# ===== OSM-Datei bestimmen =====
if [ "$PBF_URL" != "" ]; then
  echo Downloading OSM extract from "$PBF_URL"
  "${CURL[@]}" "$PBF_URL" -C - --create-dirs -o $OSMFILE
fi

if [ "$PBF_PATH" != "" ]; then
  echo Reading OSM extract from "$PBF_PATH"
  OSMFILE=$PBF_PATH
fi

# ===== PostgreSQL initialisieren =====
if [ ! -f /var/lib/postgresql/16/main/PG_VERSION ]; then
  chown postgres:postgres /var/lib/postgresql/16/main
  sudo -u postgres /usr/lib/postgresql/16/bin/initdb -D /var/lib/postgresql/16/main
fi

cp /etc/postgresql/16/main/conf.d/postgres-import.conf.disabled /etc/postgresql/16/main/conf.d/postgres-import.conf

sudo service postgresql start && \
sudo -E -u postgres psql postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='nominatim'" | grep -q 1 || sudo -E -u postgres createuser -s nominatim && \
sudo -E -u postgres psql postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='www-data'" | grep -q 1 || sudo -E -u postgres createuser -SDR www-data && \
sudo -E -u postgres psql postgres -tAc "ALTER USER nominatim WITH ENCRYPTED PASSWORD '$NOMINATIM_PASSWORD'" && \
sudo -E -u postgres psql postgres -tAc "ALTER USER \"www-data\" WITH ENCRYPTED PASSWORD '${NOMINATIM_PASSWORD}'"

chown -R nominatim:nominatim ${PROJECT_DIR}
cd ${PROJECT_DIR}

# ===== Phasenbasierter Import =====
# nominatim import --continue Optionen:
#   import-from-file -> load-data -> indexing -> db-postprocess
# --continue startet ab der angegebenen Phase und laeuft bis zum Ende durch.
# Wir merken uns die letzte abgeschlossene Phase und setzen dort fort.

if [ -f "$MARKER_DIR/all-phases.done" ]; then
    echo "Alle Import-Phasen bereits abgeschlossen."
else
    # Bestimme ab welcher Phase fortgesetzt werden muss
    if [ -f "$MARKER_DIR/04-db-postprocess.done" ]; then
        echo "Alle Phasen per Marker abgeschlossen."
        touch "$MARKER_DIR/all-phases.done"
    elif [ -f "$MARKER_DIR/03-indexing.done" ]; then
        echo "Setze fort ab Phase: db-postprocess"
        sudo -E -u nominatim nominatim import --threads $THREADS --continue db-postprocess
        touch "$MARKER_DIR/04-db-postprocess.done"
        touch "$MARKER_DIR/all-phases.done"
    elif [ -f "$MARKER_DIR/02-load-data.done" ]; then
        echo "Setze fort ab Phase: indexing"
        sudo -E -u nominatim nominatim import --threads $THREADS --continue indexing
        touch "$MARKER_DIR/03-indexing.done"
        touch "$MARKER_DIR/04-db-postprocess.done"
        touch "$MARKER_DIR/all-phases.done"
    elif [ -f "$MARKER_DIR/01-import-from-file.done" ]; then
        echo "Setze fort ab Phase: load-data"
        sudo -E -u nominatim nominatim import --threads $THREADS --continue load-data
        touch "$MARKER_DIR/02-load-data.done"
        touch "$MARKER_DIR/03-indexing.done"
        touch "$MARKER_DIR/04-db-postprocess.done"
        touch "$MARKER_DIR/all-phases.done"
    else
        echo "Starte frischen Import (Phase 1: import-from-file) ..."
        sudo -E -u postgres psql postgres -c "DROP DATABASE IF EXISTS nominatim"

        # Voller Import – wenn er komplett durchlaeuft, super.
        # Wenn er crasht, pruefen wir beim naechsten Start welche Phase erreicht wurde.
        #
        # Trick: Wir starten den vollen Import. Wenn er in einer spaeteren Phase crasht,
        # koennen wir anhand der DB pruefen ob import-from-file fertig ist.
        # Zur Sicherheit setzen wir einen Marker BEVOR load-data beginnt.
        # Da --continue import-from-file nur osm2pgsql laufen laesst, nutzen wir das:

        # Schritt 1: Nur import-from-file (erstellt DB + osm2pgsql)
        sudo -E -u nominatim nominatim import --osm-file $OSMFILE --threads $THREADS --continue import-from-file 2>&1 && {
            touch "$MARKER_DIR/01-import-from-file.done"
            echo "Phase 01-import-from-file abgeschlossen."
        } || {
            # import-from-file mit --continue braucht eine existierende DB
            # Beim allerersten Start gibt es keine DB -> normaler Import noetig
            # Wir muessen den vollen Import starten
            echo "import-from-file fehlgeschlagen (DB existiert vermutlich noch nicht)."
            echo "Starte kompletten nominatim import ..."
            sudo -E -u nominatim nominatim import --osm-file $OSMFILE --threads $THREADS
            touch "$MARKER_DIR/01-import-from-file.done"
            touch "$MARKER_DIR/02-load-data.done"
            touch "$MARKER_DIR/03-indexing.done"
            touch "$MARKER_DIR/04-db-postprocess.done"
            touch "$MARKER_DIR/all-phases.done"
        }

        # Falls Phase 1 fertig ist aber noch nicht alles durch:
        if [ ! -f "$MARKER_DIR/all-phases.done" ]; then
            echo "Phase 2: load-data"
            sudo -E -u nominatim nominatim import --threads $THREADS --continue load-data
            touch "$MARKER_DIR/02-load-data.done"
            touch "$MARKER_DIR/03-indexing.done"
            touch "$MARKER_DIR/04-db-postprocess.done"
            touch "$MARKER_DIR/all-phases.done"
        fi
    fi
fi

# ===== Nach-Import-Schritte =====
if [ -f tiger-nominatim-preprocessed.csv.tar.gz ]; then
  echo "Importing Tiger address data"
  sudo -E -u nominatim nominatim add-data --tiger-data tiger-nominatim-preprocessed.csv.tar.gz
fi

sudo -E -u nominatim nominatim index --threads $THREADS
sudo -E -u nominatim nominatim admin --check-database

if [ "$REPLICATION_URL" != "" ]; then
  sudo -E -u nominatim nominatim replication --init
  if [ "$FREEZE" = "true" ]; then
    echo "Skipping freeze because REPLICATION_URL is not empty"
  fi
else
  if [ "$FREEZE" = "true" ]; then
    echo "Freezing database"
    sudo -E -u nominatim nominatim freeze
  fi
fi

export NOMINATIM_QUERY_TIMEOUT=600
export NOMINATIM_REQUEST_TIMEOUT=3600
if [ "$REVERSE_ONLY" = "true" ]; then
  sudo -H -E -u nominatim nominatim admin --warm --reverse
else
  sudo -H -E -u nominatim nominatim admin --warm
fi
export NOMINATIM_QUERY_TIMEOUT=10
export NOMINATIM_REQUEST_TIMEOUT=60

sudo -E -u nominatim psql -d nominatim -c "ANALYZE VERBOSE"

sudo service postgresql stop

rm /etc/postgresql/16/main/conf.d/postgres-import.conf

echo "Deleting downloaded dumps in ${PROJECT_DIR}"
rm -f ${PROJECT_DIR}/*sql.gz
rm -f ${PROJECT_DIR}/*csv.gz
rm -f ${PROJECT_DIR}/tiger-nominatim-preprocessed.csv.tar.gz

if [ "$PBF_URL" != "" ]; then
  rm -f ${OSMFILE}
fi