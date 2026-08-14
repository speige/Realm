(function () {
    const vscode = acquireVsCodeApi();

    let units = {};
    let selectedUnitId = null;
    let searchQuery = '';
    let debugMode = false;
    let debugJsonExpanded = false;
    let isLocked = false;
    let undoStack = [];
    let redoStack = [];
    let isUndoRedoAction = false;
    let isInternalChange = false;
    let resolveCallbacks = {};
    let resolveCallbackId = 0;

    // DOM Elements
    const emptyState = document.getElementById('empty-state');
    const editorForm = document.getElementById('editor-form');
    const mapPropertiesForm = document.getElementById('map-properties-form');
    
    const customWeaponsForm = document.getElementById('custom-weapons-form');
    const customWeaponsList = document.getElementById('custom-weapons-list');
    const addCustomWeaponBtn = document.getElementById('add-custom-weapon-btn');
    const pasteCustomWeaponBtn = document.getElementById('paste-custom-weapon-btn');

    const customAbilitiesForm = document.getElementById('custom-abilities-form');
    const customAbilitiesList = document.getElementById('custom-abilities-list');
    const addCustomAbilityBtn = document.getElementById('add-custom-ability-btn');
    const pasteCustomAbilityBtn = document.getElementById('paste-custom-ability-btn');

    const customUpgradesForm = document.getElementById('custom-upgrades-form');
    const customUpgradesList = document.getElementById('custom-upgrades-list');
    const addCustomUpgradeBtn = document.getElementById('add-custom-upgrade-btn');
    const pasteCustomUpgradeBtn = document.getElementById('paste-custom-upgrade-btn');

    const customItemsForm = document.getElementById('custom-items-form');
    const customItemsList = document.getElementById('custom-items-list');
    const addCustomItemBtn = document.getElementById('add-custom-item-btn');
    const pasteCustomItemBtn = document.getElementById('paste-custom-item-btn');

    const unitListContainer = document.getElementById('unit-list');
    const addUnitBtn = document.getElementById('add-unit-btn');
    const searchInput = document.getElementById('search-input');
    const editorTitle = document.getElementById('editor-title');
    const editorSubtitle = document.getElementById('editor-subtitle');

    const toggleDebugBtn = document.getElementById('toggle-debug-btn');
    const toggleLockBtn = document.getElementById('toggle-lock-btn');
    const toggleButtonsBtn = document.getElementById('toggle-buttons-btn');
    const copyJsonBtn = document.getElementById('copy-json-btn');
    const expandJsonBtn = document.getElementById('expand-json-btn');
    const duplicateUnitBtn = document.getElementById('duplicate-unit-btn');
    const deleteUnitBtn = document.getElementById('delete-unit-btn');
    const copyUnitBtn = document.getElementById('copy-unit-btn');
    const pasteUnitBtn = document.getElementById('paste-unit-btn');

    const formFields = {
        UnitId: document.getElementById('field-UnitId'),
        Name: document.getElementById('field-Name'),
        Description: document.getElementById('field-Description'),
        ModelPath: document.getElementById('field-ModelPath'),
        PortraitModelPath: document.getElementById('field-PortraitModelPath'),
        YOffset: document.getElementById('field-YOffset'),
        CollisionCircle: document.getElementById('field-CollisionCircle'),
        Brightness: document.getElementById('field-Brightness'),
        Tint: document.getElementById('field-Tint'),
        RecalculateNormals: document.getElementById('field-RecalculateNormals'),
        IsHero: document.getElementById('field-IsHero'),
        MaxHp: document.getElementById('field-MaxHp'),
        Damage: document.getElementById('field-Damage'),
        Range: document.getElementById('field-Range'),
        Armor: document.getElementById('field-Armor'),
        Speed: document.getElementById('field-Speed'),
        AttackCooldown: document.getElementById('field-AttackCooldown'),
        ScanRadius: document.getElementById('field-ScanRadius'),
        CostGold: document.getElementById('field-CostGold'),
        CostWood: document.getElementById('field-CostWood'),
        CostStone: document.getElementById('field-CostStone'),
        PopCost: document.getElementById('field-PopCost'),
        ProductionTime: document.getElementById('field-ProductionTime'),
        AttackType: document.getElementById('field-AttackType'),
        ArmorType: document.getElementById('field-ArmorType'),
        GoldBounty: document.getElementById('field-GoldBounty'),
        XpBounty: document.getElementById('field-XpBounty'),
        PathingType: document.getElementById('field-PathingType'),
        MaxCapacity: document.getElementById('field-MaxCapacity'),
        HarvestRate: document.getElementById('field-HarvestRate'),
        GrowthRate: document.getElementById('field-GrowthRate'),
        MaxWorkers: document.getElementById('field-MaxWorkers')
    };

    function getArrayKeyForDomain(domain) {
        if (domain === 'buildings') return 'CustomBuildings';
        if (domain === 'resources') return 'CustomResources';
        if (domain === 'props') return 'CustomProps';
        return 'CustomUnits';
    }

    function getAllEntities() {
        if (!Array.isArray(units.CustomUnits)) units.CustomUnits = [];
        if (!Array.isArray(units.CustomBuildings)) units.CustomBuildings = [];
        if (!Array.isArray(units.CustomResources)) units.CustomResources = [];
        if (!Array.isArray(units.CustomProps)) units.CustomProps = [];
        return [
            ...units.CustomUnits,
            ...units.CustomBuildings,
            ...units.CustomResources,
            ...units.CustomProps
        ];
    }

    function getCustomUnits() {
        return getAllEntities();
    }

    function getUnitById(id) {
        return getAllEntities().find(u => u && u.UnitId === id);
    }

    const buildOptionInput = document.getElementById('build-option-input');
    const addBuildOptionBtn = document.getElementById('add-build-option-btn');
    const buildOptionsTags = document.getElementById('build-options-tags');

    const abilityInput = document.getElementById('ability-input');
    const addAbilityBtn = document.getElementById('add-ability-btn');
    const abilitiesTags = document.getElementById('abilities-tags');

    const weaponInput = document.getElementById('weapon-input');
    const addWeaponBtn = document.getElementById('add-weapon-btn');
    const weaponsTags = document.getElementById('weapons-tags');

    const itemInput = document.getElementById('item-input');
    const addItemBtn = document.getElementById('add-item-btn');
    const itemsTags = document.getElementById('items-tags');

    const upgradeInput = document.getElementById('upgrade-input');
    const addUpgradeBtn = document.getElementById('add-upgrade-btn');
    const upgradesTags = document.getElementById('upgrades-tags');

    const statuseffectInput = document.getElementById('statuseffect-input');
    const addStatuseffectBtn = document.getElementById('add-statuseffect-btn');
    const statuseffectsTags = document.getElementById('statuseffects-tags');

    const soundeventInput = document.getElementById('soundevent-input');
    const addSoundeventBtn = document.getElementById('add-soundevent-btn');
    const soundeventsTags = document.getElementById('soundevents-tags');

    const mapPropFields = {
        MapName: document.getElementById('prop-MapName'),
        MapDescription: document.getElementById('prop-MapDescription'),
        SuggestedPlayers: document.getElementById('prop-SuggestedPlayers'),
        MinimapImage: document.getElementById('prop-MinimapImage'),
        FogOfWarType: document.getElementById('prop-FogOfWarType'),
        TerrainBaseHeight: document.getElementById('prop-TerrainBaseHeight'),
        ShadowIntensity: document.getElementById('prop-ShadowIntensity'),
        MapWidth: document.getElementById('prop-MapWidth'),
        MapHeight: document.getElementById('prop-MapHeight'),
        PlayableWidth: document.getElementById('prop-PlayableWidth'),
        PlayableHeight: document.getElementById('prop-PlayableHeight'),
        LoadingImage: document.getElementById('prop-LoadingImage'),
        LoadingMusic: document.getElementById('prop-LoadingMusic'),
        LoadingTitle: document.getElementById('prop-LoadingTitle'),
        LoadingSubtitle: document.getElementById('prop-LoadingSubtitle'),
        LoadingBodyText: document.getElementById('prop-LoadingBodyText'),
        HowToPlayObjective: document.getElementById('prop-HowToPlayObjective'),
        Version: document.getElementById('prop-Version')
    };

    const instructionInput = document.getElementById('instruction-input');
    const addInstructionBtn = document.getElementById('add-instruction-btn');
    const instructionsTags = document.getElementById('instructions-tags');

    const addPlayerSlotBtn = document.getElementById('add-player-slot-btn');
    const playerSlotsList = document.getElementById('player-slots-list');

    const addTeamBtn = document.getElementById('add-team-btn');
    const teamsList = document.getElementById('teams-list');

    const addChangelogBtn = document.getElementById('add-changelog-btn');
    const changelogList = document.getElementById('changelog-list');

    function init() {
        // Switch to the units tab by default
        switchTab('units');

        // Setup global tab event listeners
        document.querySelectorAll('.tab-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const domain = btn.dataset.domain;
                switchTab(domain);
            });
        });

        // Setup global details row expand-collapse click delegation
        document.addEventListener('click', e => {
            const btn = e.target.closest('.row-expand-btn');
            if (btn) {
                const targetId = btn.dataset.target;
                const detailRow = document.getElementById(targetId);
                if (detailRow) {
                    const isHidden = detailRow.classList.contains('hidden');
                    if (isHidden) {
                        detailRow.classList.remove('hidden');
                        btn.classList.add('expanded');
                    } else {
                        detailRow.classList.add('hidden');
                        btn.classList.remove('expanded');
                    }
                }
            }
        });

        // Setup numeric keypress locking
        setupNumericLockOnDynamicInputs();

        document.getElementById('chk-show-all-glb')?.addEventListener('change', () => {
            const unit = getUnitById(selectedUnitId);
            populateGlbDropdown(getActiveDomain(), unit ? unit.ModelPath : '');
        });

        document.getElementById('field-ModelPath')?.addEventListener('change', e => {
            const unit = getUnitById(selectedUnitId);
            if (unit) {
                unit.ModelPath = e.target.value;
                saveChanges();
                updateAllThumbnails();
            }
        });

        // Notify VS Code that frontend is ready
        vscode.postMessage({ type: 'ready' });
    }

    // switchTab handles toggling between domains
    function getActiveDomain() {
        const activeBtn = document.querySelector('.tab-btn.active');
        return activeBtn ? activeBtn.dataset.domain : 'units';
    }

    function matchesDomainCategory(unit, domain) {
        if (!unit) return false;
        return true;
    }

    function switchTab(domain) {
        document.querySelectorAll('.tab-btn').forEach(b => {
            b.classList.toggle('active', b.dataset.domain === domain);
        });

        const appContainer = document.querySelector('.app-container');
        const isEntityDomain = (domain === 'units' || domain === 'buildings' || domain === 'resources' || domain === 'props');

        if (isEntityDomain) {
            appContainer.classList.remove('sidebar-hidden');
            const headerTitle = document.querySelector('.sidebar-subheader h2');
            if (headerTitle) {
                headerTitle.textContent = domain === 'units' ? 'Units List' :
                                         domain === 'buildings' ? 'Buildings List' :
                                         domain === 'resources' ? 'Resources List' : 'Props List';
            }
            renderUnitList();
            const matchingUnits = domain === 'buildings' ? (units.CustomBuildings || []) :
                                  domain === 'resources' ? (units.CustomResources || []) :
                                  domain === 'props' ? (units.CustomProps || []) :
                                  (units.CustomUnits || []);
            if (matchingUnits.length > 0) {
                if (!selectedUnitId || !matchingUnits.some(u => u.UnitId === selectedUnitId)) {
                    selectUnit(matchingUnits[0].UnitId);
                } else {
                    selectUnit(selectedUnitId);
                }
            } else {
                selectedUnitId = null;
                showEmptyState();
            }
        } else {
            appContainer.classList.add('sidebar-hidden');
            if (domain === 'weapons') selectCustomWeapons();
            else if (domain === 'abilities') selectCustomAbilities();
            else if (domain === 'upgrades') selectCustomUpgrades();
            else if (domain === 'items') selectCustomItems();
            else if (domain === 'assets') selectCustomAssets();
            else if (domain === 'properties') selectMapProperties();
        }
    }

    // listen to messaging from the extension
    window.addEventListener('message', event => {
        const message = event.data;
        switch (message.type) {
            case 'update':
                const oldUnitsStr = serializeDeterministic(units);
                if (message.text !== oldUnitsStr) {
                    if (!isUndoRedoAction && !isInternalChange) {
                        undoStack = [];
                        redoStack = [];
                    }
                }
                try {
                    units = message.text ? JSON.parse(message.text) : {};
                } catch (e) {
                    units = {};
                }
                if (!units.MapProperties) {
                    units.MapProperties = {};
                }
                let migrated = false;
                if (units.MapProperties.CustomWeapons) {
                    units.CustomWeapons = units.MapProperties.CustomWeapons;
                    delete units.MapProperties.CustomWeapons;
                    migrated = true;
                }
                if (units.MapProperties.CustomAbilities) {
                    units.CustomAbilities = units.MapProperties.CustomAbilities;
                    delete units.MapProperties.CustomAbilities;
                    migrated = true;
                }
                if (units.MapProperties.CustomUpgrades) {
                    units.CustomUpgrades = units.MapProperties.CustomUpgrades;
                    delete units.MapProperties.CustomUpgrades;
                    migrated = true;
                }
                if (units.MapProperties.CustomItems) {
                    units.CustomItems = units.MapProperties.CustomItems;
                    delete units.MapProperties.CustomItems;
                    migrated = true;
                }
                if (!Array.isArray(units.CustomWeapons)) units.CustomWeapons = units.CustomWeapons || [];
                if (!Array.isArray(units.CustomAbilities)) units.CustomAbilities = units.CustomAbilities || [];
                if (!Array.isArray(units.CustomUpgrades)) units.CustomUpgrades = units.CustomUpgrades || [];
                if (!Array.isArray(units.CustomItems)) units.CustomItems = units.CustomItems || [];

                if (!Array.isArray(units.CustomUnits)) units.CustomUnits = [];
                if (!Array.isArray(units.CustomBuildings)) units.CustomBuildings = [];
                if (!Array.isArray(units.CustomResources)) units.CustomResources = [];
                if (!Array.isArray(units.CustomProps)) units.CustomProps = [];

                const knownTopKeys = [
                    'MapProperties', 'CustomUnits', 'CustomBuildings', 'CustomResources', 'CustomProps',
                    'CustomAbilities', 'CustomItems', 'CustomUpgrades', 'CustomWeapons', 'Assets', 
                    'ModelOffsets', 'ModelCollisionCircleRatios', 'ModelBrightness', 'ModelGenerateNormals'
                ];
                for (const [key, val] of Object.entries(units)) {
                    if (!knownTopKeys.includes(key) && val && typeof val === 'object' && !Array.isArray(val) && (val.UnitId || val.MaxHp !== undefined || val.CostGold !== undefined || val.AttackType !== undefined || val.PathingCapabilities || val.MovementType)) {
                        if (!val.UnitId) val.UnitId = key;
                        const armorType = (val.ArmorType || '').toLowerCase();
                        if (armorType === 'building') units.CustomBuildings.push(val);
                        else units.CustomUnits.push(val);
                        delete units[key];
                        migrated = true;
                    }
                }

                for (const u of getAllEntities()) {
                    if (u.PathingType === undefined || u.PathingType === null) {
                        if (u.MovementType === 'air' || u.MovementType === 'flying') {
                            u.PathingType = 4;
                        } else if (u.MovementType === 'amphibious') {
                            u.PathingType = 9;
                        } else {
                            u.PathingType = (u.ArmorType === 'building') ? 32 : 8;
                        }
                    }
                    delete u.MovementType;
                    delete u.PathingCapabilities;
                    delete u.DefaultAssetType;
                }

                if (migrated) {
                    saveChanges();
                }

                renderAssetsMetadata();
                if (serializeDeterministic(units) !== oldUnitsStr) {
                    renderUnitList();
                    if (selectedUnitId === '__map_properties__') {
                        populateMapProperties();
                    } else if (selectedUnitId === '__custom_weapons__') {
                        renderCustomWeapons();
                    } else if (selectedUnitId === '__custom_abilities__') {
                        renderCustomAbilities();
                    } else if (selectedUnitId === '__custom_upgrades__') {
                        renderCustomUpgrades();
                    } else if (selectedUnitId === '__custom_items__') {
                        renderCustomItems();
                    } else if (selectedUnitId === '__custom_assets__') {
                        renderAssetsMetadata();
                    } else if (selectedUnitId && getUnitById(selectedUnitId)) {
                        populateForm(selectedUnitId);
                    } else {
                        // Keep current domain tab selection
                        const activeTabBtn = document.querySelector('.tab-btn.active');
                        if (activeTabBtn) {
                            switchTab(activeTabBtn.dataset.domain);
                        } else {
                            selectedUnitId = null;
                            showEmptyState();
                        }
                    }
                } else if (selectedUnitId === '__custom_assets__') {
                    renderAssetsMetadata();
                }
                updateDebugJson();
                updateCatalogCardErrors();
                applyLockState();
                updateAllThumbnails();
                updateDatalists();
                break;
            case 'browseFileResult':
                let targetInput = null;
                if (message.fieldId) {
                    targetInput = document.getElementById(message.fieldId);
                } else if (message.fieldClass) {
                    const selector = `input.${message.fieldClass.split(' ').join('.')}[data-index="${message.fieldIndex}"]`;
                    targetInput = document.querySelector(selector);
                }
                if (targetInput) {
                    targetInput.value = message.path;
                    const event = new Event('change', { bubbles: true });
                    targetInput.dispatchEvent(event);
                    updateThumbnailForInput(targetInput);
                }
                break;
            case 'browseFileFallback':
                const fileInput = document.createElement('input');
                fileInput.type = 'file';
                if (message.accept) {
                    fileInput.accept = message.accept;
                }
                fileInput.style.display = 'none';
                fileInput.addEventListener('change', () => {
                    if (fileInput.files && fileInput.files[0]) {
                        const selectedFile = fileInput.files[0];
                        let filePath = selectedFile.name;
                        let targetInp = null;
                        if (message.fieldId) {
                            targetInp = document.getElementById(message.fieldId);
                        } else if (message.fieldClass) {
                            const selector = `input.${message.fieldClass.split(' ').join('.')}[data-index="${message.fieldIndex}"]`;
                            targetInp = document.querySelector(selector);
                        }
                        if (targetInp) {
                            targetInp.value = filePath;
                            const evt = new Event('change', { bubbles: true });
                            targetInp.dispatchEvent(evt);
                            updateThumbnailForInput(targetInp);
                        }
                    }
                    fileInput.remove();
                });
                document.body.appendChild(fileInput);
                fileInput.click();
                break;
            case 'importAssetFallback':
                const assetInput = document.createElement('input');
                assetInput.type = 'file';
                if (message.accept) {
                    assetInput.accept = message.accept;
                }
                assetInput.style.display = 'none';
                assetInput.addEventListener('change', () => {
                    if (assetInput.files && assetInput.files[0]) {
                        const selectedFile = assetInput.files[0];
                        if (message.assetType === 'vfx') {
                            const img = new Image();
                            const url = URL.createObjectURL(selectedFile);
                            img.onload = () => {
                                const canvas = document.createElement('canvas');
                                canvas.width = img.width;
                                canvas.height = img.height;
                                const ctx = canvas.getContext('2d');
                                ctx.drawImage(img, 0, 0);
                                canvas.toBlob((blob) => {
                                    URL.revokeObjectURL(url);
                                    if (blob) {
                                        const r = new FileReader();
                                        r.onload = () => {
                                            const arrayBuffer = r.result;
                                            const bytes = new Uint8Array(arrayBuffer);
                                            let binaryString = '';
                                            for (let i = 0; i < bytes.byteLength; i++) {
                                                binaryString += String.fromCharCode(bytes[i]);
                                            }
                                            const base64Data = btoa(binaryString);
                                            const baseName = selectedFile.name.substring(0, selectedFile.name.lastIndexOf('.')) || selectedFile.name;
                                            vscode.postMessage({
                                                type: 'processImportedAsset',
                                                fileName: baseName + '.png',
                                                fileDataBase64: base64Data,
                                                assetType: message.assetType,
                                                options: message.extraOptions
                                            });
                                        };
                                        r.readAsArrayBuffer(blob);
                                    }
                                }, 'image/png');
                            };
                            img.onerror = () => {
                                URL.revokeObjectURL(url);
                            };
                            img.src = url;
                        } else {
                            const reader = new FileReader();
                            reader.onload = () => {
                                const arrayBuffer = reader.result;
                                const bytes = new Uint8Array(arrayBuffer);
                                let binaryString = '';
                                for (let i = 0; i < bytes.byteLength; i++) {
                                    binaryString += String.fromCharCode(bytes[i]);
                                }
                                const base64Data = btoa(binaryString);
                                vscode.postMessage({
                                    type: 'processImportedAsset',
                                    fileName: selectedFile.name,
                                    fileDataBase64: base64Data,
                                    assetType: message.assetType,
                                    options: message.extraOptions
                                });
                            };
                            reader.readAsArrayBuffer(selectedFile);
                        }
                    }
                    assetInput.remove();
                });
                document.body.appendChild(assetInput);
                assetInput.click();
            case 'resolvePathResult':
                const callback = resolveCallbacks[message.requestId];
                if (callback) {
                    callback(message.uri);
                    delete resolveCallbacks[message.requestId];
                }
                break;
        }
    });

    let saveTimeout = null;
    function saveChanges() {
        showSaving();
        isInternalChange = true;
        vscode.postMessage({
            type: 'change',
            text: serializeDeterministic(units)
        });
        updateDebugJson();
        updateCatalogCardErrors();
        
        if (saveTimeout) clearTimeout(saveTimeout);
        saveTimeout = setTimeout(() => {
            showSaved();
            isInternalChange = false;
        }, 500);
    }

    function renderUnitList() {
        unitListContainer.innerHTML = '';
        const query = searchQuery.toLowerCase();
        const activeDomain = getActiveDomain();

        const customUnitsList = activeDomain === 'buildings' ? (units.CustomBuildings || []) :
                                activeDomain === 'resources' ? (units.CustomResources || []) :
                                activeDomain === 'props' ? (units.CustomProps || []) :
                                (units.CustomUnits || []);
        for (const unit of customUnitsList) {
            if (!unit || !unit.UnitId) continue;
            const id = unit.UnitId;
            const name = unit.Name || '';
            const desc = unit.Description || '';

            if (query && !id.toLowerCase().includes(query) && !name.toLowerCase().includes(query) && !desc.toLowerCase().includes(query)) {
                continue;
            }

            const card = document.createElement('div');
            card.className = `unit-card${selectedUnitId === id ? ' active' : ''}`;
            card.dataset.id = id;

            const header = document.createElement('div');
            header.className = 'unit-card-header';

            const title = document.createElement('div');
            title.className = 'unit-card-title';
            title.textContent = name || id;

            const unitIdSpan = document.createElement('span');
            unitIdSpan.className = 'unit-card-id';
            unitIdSpan.textContent = id;

            header.appendChild(title);
            header.appendChild(unitIdSpan);

            const descDiv = document.createElement('div');
            descDiv.className = 'unit-card-desc';
            descDiv.textContent = desc;

            const badges = document.createElement('div');
            badges.className = 'unit-card-badges';

            const validation = getValidationErrors();
            const unitErrors = validation.units[id];
            if (unitErrors && Object.keys(unitErrors).length > 0) {
                const b = document.createElement('span');
                b.className = 'badge badge-error';
                b.textContent = '⚠️ Invalid Refs';
                b.title = Object.values(unitErrors).join('\n');
                badges.appendChild(b);
            }

            if (unit.CostGold > 0) {
                const b = document.createElement('span');
                b.className = 'badge badge-gold';
                b.textContent = `${unit.CostGold}G`;
                badges.appendChild(b);
            }
            if (unit.CostWood > 0) {
                const b = document.createElement('span');
                b.className = 'badge badge-wood';
                b.textContent = `${unit.CostWood}W`;
                badges.appendChild(b);
            }
            if (unit.CostStone > 0) {
                const b = document.createElement('span');
                b.className = 'badge badge-stone';
                b.textContent = `${unit.CostStone}S`;
                badges.appendChild(b);
            }
            if (unit.AttackType && unit.AttackType !== 'none') {
                const b = document.createElement('span');
                b.className = 'badge badge-attack';
                b.textContent = unit.AttackType;
                badges.appendChild(b);
            }

            card.appendChild(header);
            card.appendChild(descDiv);
            if (badges.children.length > 0) {
                card.appendChild(badges);
            }

            card.addEventListener('click', () => {
                selectUnit(id);
            });

            unitListContainer.appendChild(card);
        }
    }

    function selectUnit(id) {
        selectedUnitId = id;
        hideAllForms();
        renderUnitList();
        populateForm(id);
    }

    function selectMapProperties() {
        selectedUnitId = '__map_properties__';
        hideAllForms();
        mapPropertiesForm.classList.remove('hidden');
        populateMapProperties();
    }

    // Spreadsheet Selection Toggles
    function selectCustomWeapons() {
        selectedUnitId = '__custom_weapons__';
        hideAllForms();
        customWeaponsForm.classList.remove('hidden');
        renderCustomWeapons();
    }

    function selectCustomAbilities() {
        selectedUnitId = '__custom_abilities__';
        hideAllForms();
        customAbilitiesForm.classList.remove('hidden');
        renderCustomAbilities();
    }

    function selectCustomUpgrades() {
        selectedUnitId = '__custom_upgrades__';
        hideAllForms();
        customUpgradesForm.classList.remove('hidden');
        renderCustomUpgrades();
    }

    function selectCustomItems() {
        selectedUnitId = '__custom_items__';
        hideAllForms();
        customItemsForm.classList.remove('hidden');
        renderCustomItems();
    }

    function hideAllForms() {
        emptyState.classList.add('hidden');
        editorForm.classList.add('hidden');
        mapPropertiesForm.classList.add('hidden');
        customWeaponsForm.classList.add('hidden');
        customAbilitiesForm.classList.add('hidden');
        customUpgradesForm.classList.add('hidden');
        customItemsForm.classList.add('hidden');
        const customAssetsForm = document.getElementById('custom-assets-form');
        if (customAssetsForm) customAssetsForm.classList.add('hidden');
    }

    function populateForm(id) {
        const unit = getUnitById(id);
        if (!unit) {
            showEmptyState();
            return;
        }

        hideAllForms();
        editorForm.classList.remove('hidden');
        editorTitle.textContent = unit.Name || id;
        editorSubtitle.textContent = `ID: ${id}`;
        
        const activeDomain = getActiveDomain();
        const categoryTitle = activeDomain.charAt(0).toUpperCase() + activeDomain.slice(1);
        const breadcrumb = document.getElementById('editor-breadcrumb');
        if (breadcrumb) {
            breadcrumb.textContent = `${categoryTitle} > ${id}`;
        }

        for (const [key, element] of Object.entries(formFields)) {
            if (!element) continue;
            
            const val = unit[key];
            if (element.type === 'checkbox') {
                element.checked = !!val;
            } else if (val === undefined || val === null) {
                element.value = '';
            } else {
                element.value = val;
            }
        }

        const defaultPathingVal = (activeDomain === 'resources' || activeDomain === 'props') ? 255 : (activeDomain === 'buildings') ? 32 : 8;
        const pathingVal = unit.PathingType !== undefined ? unit.PathingType : defaultPathingVal;
        document.querySelectorAll('.pathing-flag-cb').forEach(cb => {
            const flagVal = parseInt(cb.value, 10);
            cb.checked = (pathingVal & flagVal) !== 0;
        });

        const resConfigSection = document.getElementById('section-resource-node-config');
        const unitStatsSection = document.getElementById('section-unit-stats');
        const unitCostSection = document.getElementById('section-unit-costs');
        const unitCombatSection = document.getElementById('section-unit-combat');
        const unitCapabilitiesSection = document.getElementById('section-unit-capabilities');
        const pathingSection = document.getElementById('section-pathing-flags');
        const isHeroGroup = document.getElementById('field-IsHero')?.closest('.form-group');
        const portraitGroup = document.getElementById('field-PortraitModelPath')?.closest('.form-group');

        if (activeDomain === 'props') {
            if (resConfigSection) resConfigSection.classList.add('hidden');
            if (unitStatsSection) unitStatsSection.classList.add('hidden');
            if (unitCostSection) unitCostSection.classList.add('hidden');
            if (unitCombatSection) unitCombatSection.classList.add('hidden');
            if (unitCapabilitiesSection) unitCapabilitiesSection.classList.add('hidden');
            if (pathingSection) pathingSection.classList.remove('hidden');
            if (isHeroGroup) isHeroGroup.classList.add('hidden');
            if (portraitGroup) portraitGroup.classList.remove('hidden');
        } else if (activeDomain === 'resources') {
            if (resConfigSection) resConfigSection.classList.remove('hidden');
            if (unitStatsSection) unitStatsSection.classList.add('hidden');
            if (unitCostSection) unitCostSection.classList.add('hidden');
            if (unitCombatSection) unitCombatSection.classList.add('hidden');
            if (unitCapabilitiesSection) unitCapabilitiesSection.classList.add('hidden');
            if (pathingSection) pathingSection.classList.remove('hidden');
            if (isHeroGroup) isHeroGroup.classList.add('hidden');
            if (portraitGroup) portraitGroup.classList.remove('hidden');
        } else {
            if (resConfigSection) resConfigSection.classList.add('hidden');
            if (unitStatsSection) unitStatsSection.classList.remove('hidden');
            if (unitCostSection) unitCostSection.classList.remove('hidden');
            if (unitCombatSection) unitCombatSection.classList.remove('hidden');
            if (unitCapabilitiesSection) unitCapabilitiesSection.classList.remove('hidden');
            if (pathingSection) pathingSection.classList.remove('hidden');
            if (isHeroGroup) isHeroGroup.classList.remove('hidden');
            if (portraitGroup) portraitGroup.classList.remove('hidden');
        }

        renderTags('build-options', unit.BuildOptions || []);
        renderTags('abilities', unit.Abilities || []);
        renderTags('weapons', unit.Weapons || []);
        renderTags('items', unit.StartingItems || []);
        renderTags('upgrades', unit.Upgrades || []);
        renderTags('statuseffects', unit.StatusEffects || []);
        renderTags('soundevents', unit.SoundEvents || []);
        
        populateGlbDropdown(activeDomain, unit.ModelPath || '');
        updateAllThumbnails();
    }

    function populateGlbDropdown(domain, currentModelPath) {
        const select = document.getElementById('field-ModelPath');
        if (!select) return;
        select.innerHTML = '';

        const noneOpt = document.createElement('option');
        noneOpt.value = '';
        noneOpt.textContent = '-- None (Default Model) --';
        select.appendChild(noneOpt);

        const showAll = document.getElementById('chk-show-all-glb')?.checked ?? false;
        const targetCategory = domain === 'units' ? 'units' :
                              domain === 'buildings' ? 'buildings' :
                              domain === 'resources' ? 'resources' :
                              domain === 'props' ? 'props' : null;

        const glbAssets = (units.Assets && units.Assets.glb) ? units.Assets.glb : {};
        for (const [catKey, catObj] of Object.entries(glbAssets)) {
            if (!catObj || typeof catObj !== 'object') continue;
            const normCat = catKey.toLowerCase();

            if (!showAll && targetCategory) {
                const isMatch = normCat === targetCategory || (targetCategory === 'buildings' && normCat === 'building');
                if (!isMatch) continue;
            }

            for (const [fileName, val] of Object.entries(catObj)) {
                const opt = document.createElement('option');
                opt.value = fileName;
                opt.textContent = `${fileName} (${normCat})`;
                if (fileName === currentModelPath) {
                    opt.selected = true;
                }
                select.appendChild(opt);
            }
        }

        if (currentModelPath && ![...select.options].some(o => o.value === currentModelPath)) {
            const customOpt = document.createElement('option');
            customOpt.value = currentModelPath;
            customOpt.textContent = `${currentModelPath} (custom)`;
            customOpt.selected = true;
            select.appendChild(customOpt);
        }
    }

    function populateMapProperties() {
        hideAllForms();
        mapPropertiesForm.classList.remove('hidden');
        const props = units.MapProperties || {};

        for (const [key, element] of Object.entries(mapPropFields)) {
            if (!element) continue;

            const val = props[key];
            if (element.type === 'checkbox') {
                element.checked = !!val;
            } else if (val === undefined || val === null) {
                element.value = '';
            } else {
                element.value = val;
            }
        }

        renderTags('instructions', props.HowToPlayInstructions || []);
        renderPlayerSlots();
        renderTeams();
        renderChangelog();
    }

    function renderTags(type, list) {
        let container;
        if (type === 'build-options') container = buildOptionsTags;
        else if (type === 'abilities') container = abilitiesTags;
        else if (type === 'instructions') container = instructionsTags;
        else if (type === 'weapons') container = weaponsTags;
        else if (type === 'items') container = itemsTags;
        else if (type === 'upgrades') container = upgradesTags;
        else if (type === 'statuseffects') container = statuseffectsTags;
        else if (type === 'soundevents') container = soundeventsTags;

        if (!container) return;
        container.innerHTML = '';

        const validation = getValidationErrors();
        const unitErrors = selectedUnitId ? validation.units[selectedUnitId] : null;

        list.forEach((item, index) => {
            const tag = document.createElement('span');
            tag.className = 'tag';
            tag.textContent = item;

            let errorMsg = null;
            if (type === 'build-options' && unitErrors && unitErrors[`BuildOptions_${index}`]) {
                errorMsg = unitErrors[`BuildOptions_${index}`];
            } else if (type === 'abilities' && unitErrors && unitErrors[`Abilities_${index}`]) {
                errorMsg = unitErrors[`Abilities_${index}`];
            } else if (type === 'weapons' && unitErrors && unitErrors[`Weapons_${index}`]) {
                errorMsg = unitErrors[`Weapons_${index}`];
            } else if (type === 'items' && unitErrors && unitErrors[`StartingItems_${index}`]) {
                errorMsg = unitErrors[`StartingItems_${index}`];
            } else if (type === 'upgrades' && unitErrors && unitErrors[`Upgrades_${index}`]) {
                errorMsg = unitErrors[`Upgrades_${index}`];
            }

            let tooltipText = '';
            const details = getTooltipDetails(type, item);
            if (details) {
                tooltipText = `${details.title}\n${details.desc}`;
            }

            if (errorMsg) {
                tag.classList.add('tag-warning');
                tooltipText = errorMsg + (tooltipText ? `\n\n-- Info --\n${tooltipText}` : '');

                const warningSpan = document.createElement('span');
                warningSpan.className = 'tag-warning-icon';
                warningSpan.textContent = '⚠️ ';
                tag.insertBefore(warningSpan, tag.firstChild);
            }

            if (tooltipText) {
                tag.title = tooltipText;
            }

            const removeSpan = document.createElement('span');
            removeSpan.className = 'remove-tag';
            removeSpan.textContent = ' ×';
            removeSpan.addEventListener('click', () => {
                removeTagItem(type, index);
            });

            tag.appendChild(removeSpan);
            container.appendChild(tag);
        });
    }

    function removeTagItem(type, index) {
        if (type === 'instructions') {
            if (!units.MapProperties) units.MapProperties = {};
            const list = units.MapProperties.HowToPlayInstructions || [];
            list.splice(index, 1);
            units.MapProperties.HowToPlayInstructions = list;
            saveChanges();
            renderTags(type, list);
            return;
        }

        const unit = getUnitById(selectedUnitId);
        if (!selectedUnitId || !unit) return;
        
        let key;
        if (type === 'build-options') key = 'BuildOptions';
        else if (type === 'abilities') key = 'Abilities';
        else if (type === 'weapons') key = 'Weapons';
        else if (type === 'items') key = 'StartingItems';
        else if (type === 'upgrades') key = 'Upgrades';
        else if (type === 'statuseffects') key = 'StatusEffects';
        else if (type === 'soundevents') key = 'SoundEvents';

        if (key && unit[key]) {
            pushToUndoStack();
            unit[key].splice(index, 1);
            saveChanges();
            renderTags(type, unit[key]);
        }
    }

    function addTagItem(type) {
        if (isLocked) return;

        let inputEl, key;
        if (type === 'build-options') { inputEl = buildOptionInput; key = 'BuildOptions'; }
        else if (type === 'abilities') { inputEl = abilityInput; key = 'Abilities'; }
        else if (type === 'weapons') { inputEl = weaponInput; key = 'Weapons'; }
        else if (type === 'items') { inputEl = itemInput; key = 'StartingItems'; }
        else if (type === 'upgrades') { inputEl = upgradeInput; key = 'Upgrades'; }
        else if (type === 'statuseffects') { inputEl = statuseffectInput; key = 'StatusEffects'; }
        else if (type === 'soundevents') { inputEl = soundeventInput; key = 'SoundEvents'; }
        else if (type === 'instructions') {
            if (!units.MapProperties) units.MapProperties = {};
            const val = instructionInput.value.trim();
            if (val) {
                pushToUndoStack();
                const list = units.MapProperties.HowToPlayInstructions || [];
                list.push(val);
                units.MapProperties.HowToPlayInstructions = list;
                instructionInput.value = '';
                saveChanges();
                renderTags(type, list);
            }
            return;
        }

        const unit = getUnitById(selectedUnitId);
        if (!selectedUnitId || !unit || !inputEl || !key) return;

        const val = inputEl.value.trim();
        if (val) {
            pushToUndoStack();
            if (!unit[key]) {
                unit[key] = [];
            }
            unit[key].push(val);
            inputEl.value = '';
            saveChanges();
            renderTags(type, unit[key]);
        }
    }

    // Player slots rendering
    function renderPlayerSlots() {
        playerSlotsList.innerHTML = '';
        const list = units.MapProperties.PlayerSlots || [];

        list.forEach((slot, index) => {
            const card = document.createElement('div');
            card.className = 'list-item-card';

            card.innerHTML = `
                <div class="list-item-header">
                    <h4>Player Slot ${slot.SlotId !== undefined ? slot.SlotId : index + 1}</h4>
                    <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Slot ID</label>
                        <input type="number" class="slot-id" data-index="${index}" value="${slot.SlotId !== undefined ? slot.SlotId : index + 1}" min="0" step="1" required />
                    </div>
                    <div class="form-group">
                        <label>Name</label>
                        <input type="text" class="slot-name" data-index="${index}" value="${slot.Name || ''}" />
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Color</label>
                        <input type="text" class="slot-color" data-index="${index}" value="${slot.Color || ''}" placeholder="e.g. red, blue" />
                    </div>
                    <div class="form-group">
                        <label>Faction</label>
                        <input type="text" class="slot-faction" data-index="${index}" value="${slot.Faction || ''}" placeholder="e.g. human, elf" />
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Controller</label>
                        <select class="slot-controller" data-index="${index}">
                            <option value="HumanPlayer" ${slot.Controller === 'HumanPlayer' ? 'selected' : ''}>Human Player</option>
                            <option value="ComputerAi" ${slot.Controller === 'ComputerAi' ? 'selected' : ''}>Computer AI</option>
                            <option value="Neutral" ${slot.Controller === 'Neutral' ? 'selected' : ''}>Neutral</option>
                            <option value="Hostile" ${slot.Controller === 'Hostile' ? 'selected' : ''}>Hostile</option>
                            <option value="Open" ${slot.Controller === 'Open' ? 'selected' : ''}>Open</option>
                            <option value="Closed" ${slot.Controller === 'Closed' ? 'selected' : ''}>Closed</option>
                        </select>
                    </div>
                    <div class="form-group">
                        <label>AI Type (Optional)</label>
                        <input type="text" class="slot-aitype" data-index="${index}" value="${slot.AiType || ''}" />
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Start Location Node ID</label>
                        <input type="text" class="slot-start" data-index="${index}" value="${slot.StartLocation || ''}" placeholder="e.g. start_loc_1" />
                    </div>
                    <div class="form-group">
                        <label>Custom Decal (Optional)</label>
                        <input type="text" class="slot-decal" data-index="${index}" value="${slot.CustomDecal || ''}" />
                    </div>
                </div>
            `;

            card.querySelector('.btn-delete').addEventListener('click', () => {
                if (confirm('Are you sure you want to delete this player slot?')) {
                    pushToUndoStack();
                    list.splice(index, 1);
                    units.MapProperties.PlayerSlots = list;
                    saveChanges();
                    renderPlayerSlots();
                    renderTeams(); // Slot indexes changed, redraw teams checklists
                }
            });

            card.querySelectorAll('input, select').forEach(input => {
                input.addEventListener('change', e => {
                    const idx = parseInt(e.target.dataset.index, 10);
                    const target = e.target;
                    const val = target.value;

                    if (target.classList.contains('slot-id')) list[idx].SlotId = parseInt(val, 10) || 0;
                    else if (target.classList.contains('slot-name')) list[idx].Name = val;
                    else if (target.classList.contains('slot-color')) list[idx].Color = val;
                    else if (target.classList.contains('slot-faction')) list[idx].Faction = val;
                    else if (target.classList.contains('slot-controller')) {
                        list[idx].Controller = val;
                    }
                    else if (target.classList.contains('slot-aitype')) list[idx].AiType = val;
                    else if (target.classList.contains('slot-start')) list[idx].StartLocation = val;
                    else if (target.classList.contains('slot-decal')) list[idx].CustomDecal = val;

                    units.MapProperties.PlayerSlots = list;
                    saveChanges();
                });
            });

            playerSlotsList.appendChild(card);
        });
        setupNumericLockOnDynamicInputs();
    }

    // Teams rendering
    function renderTeams() {
        teamsList.innerHTML = '';
        const list = units.MapProperties.Teams || [];
        const slots = units.MapProperties.PlayerSlots || [];

        list.forEach((team, index) => {
            const card = document.createElement('div');
            card.className = 'list-item-card';

            const slotCheckboxes = slots.map(slot => {
                const checked = (team.Slots || []).includes(slot.SlotId);
                return `
                    <div class="team-slot-checkbox">
                        <input type="checkbox" id="team-${index}-slot-${slot.SlotId}" data-team-index="${index}" data-slot-id="${slot.SlotId}" ${checked ? 'checked' : ''} />
                        <label for="team-${index}-slot-${slot.SlotId}">${slot.Name || `Player Slot ${slot.SlotId}`} (ID: ${slot.SlotId})</label>
                    </div>
                `;
            }).join('');

            card.innerHTML = `
                <div class="list-item-header">
                    <h4>Team ${index + 1}</h4>
                    <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                </div>
                <div class="form-group">
                    <label>Team Name</label>
                    <input type="text" class="team-name" data-index="${index}" value="${team.TeamName || ''}" required />
                </div>
                <div class="team-slots-checkboxes">
                    <label>Assigned Player Slots</label>
                    <div class="checkbox-grid">
                        ${slotCheckboxes || '<div class="no-slots-info">Create player slots first to assign teams.</div>'}
                    </div>
                </div>
            `;

            card.querySelector('.btn-delete').addEventListener('click', () => {
                if (confirm('Are you sure you want to delete this team?')) {
                    pushToUndoStack();
                    list.splice(index, 1);
                    units.MapProperties.Teams = list;
                    saveChanges();
                    renderTeams();
                }
            });

            card.querySelector('.team-name').addEventListener('change', e => {
                const idx = parseInt(e.target.dataset.index, 10);
                list[idx].TeamName = e.target.value;
                units.MapProperties.Teams = list;
                saveChanges();
            });

            card.querySelectorAll('.checkbox-grid input[type="checkbox"]').forEach(chk => {
                chk.addEventListener('change', e => {
                    const teamIdx = parseInt(e.target.dataset.teamIndex, 10);
                    const slotId = parseInt(e.target.dataset.slotId, 10);
                    
                    if (!list[teamIdx].Slots) {
                        list[teamIdx].Slots = [];
                    }
                    
                    if (e.target.checked) {
                        if (!list[teamIdx].Slots.includes(slotId)) {
                            list[teamIdx].Slots.push(slotId);
                        }
                    } else {
                        const sIdx = list[teamIdx].Slots.indexOf(slotId);
                        if (sIdx !== -1) {
                            list[teamIdx].Slots.splice(sIdx, 1);
                        }
                    }
                    
                    units.MapProperties.Teams = list;
                    saveChanges();
                });
            });

            teamsList.appendChild(card);
        });
    }

    // Changelog rendering
    function renderChangelog() {
        changelogList.innerHTML = '';
        const list = units.MapProperties.Changelog || [];

        list.forEach((item, index) => {
            const card = document.createElement('div');
            card.className = 'list-item-card';

            card.innerHTML = `
                <div class="list-item-header">
                    <h4>Changelog Entry ${index + 1}</h4>
                    <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>Version</label>
                        <input type="text" class="log-version" data-index="${index}" value="${item.Version || ''}" placeholder="e.g. 1.0.1" required />
                    </div>
                    <div class="form-group">
                        <label>Release Date</label>
                        <input type="text" class="log-date" data-index="${index}" value="${item.Date || ''}" placeholder="YYYY-MM-DD" required />
                    </div>
                </div>
                <div class="form-group">
                    <label>Details</label>
                    <textarea class="log-details" data-index="${index}" rows="2" required>${item.Details || ''}</textarea>
                </div>
            `;

            card.querySelector('.btn-delete').addEventListener('click', () => {
                if (confirm('Are you sure you want to delete this changelog entry?')) {
                    pushToUndoStack();
                    list.splice(index, 1);
                    units.MapProperties.Changelog = list;
                    saveChanges();
                    renderChangelog();
                }
            });

            card.querySelectorAll('input, textarea').forEach(input => {
                input.addEventListener('change', e => {
                    const idx = parseInt(e.target.dataset.index, 10);
                    const target = e.target;
                    const val = target.value;

                    if (target.classList.contains('log-version')) list[idx].Version = val;
                    else if (target.classList.contains('log-date')) list[idx].Date = val;
                    else if (target.classList.contains('log-details')) list[idx].Details = val;

                    units.MapProperties.Changelog = list;
                    saveChanges();
                });
            });

            changelogList.appendChild(card);
        });
    }

    // --- SPREADSHEET TAB RENDERING ---
    
    // Custom Weapons
    function renderCustomWeapons() {
        customWeaponsList.innerHTML = '';
        const list = units.CustomWeapons || [];

        const tableContainer = document.createElement('div');
        tableContainer.className = 'spreadsheet-container';

        let tbodyContent = '';
        list.forEach((item, index) => {
            tbodyContent += `
                <tr class="main-row" data-index="${index}">
                    <td>
                        <button type="button" class="row-expand-btn" data-target="weapon-detail-${index}">▶</button>
                    </td>
                    <td>
                        <input type="text" class="weapon-id" data-index="${index}" value="${item.WeaponId || ''}" required />
                    </td>
                    <td>
                        <input type="text" class="weapon-name" data-index="${index}" value="${item.Name || ''}" />
                    </td>
                    <td>
                        <input type="number" class="weapon-damage" data-index="${index}" value="${item.Damage || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="weapon-range" data-index="${index}" value="${item.Range || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="weapon-cooldown" data-index="${index}" value="${item.AttackCooldown || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <select class="weapon-type" data-index="${index}">
                            <option value="melee" ${item.AttackType === 'melee' ? 'selected' : ''}>Melee</option>
                            <option value="ranged" ${item.AttackType === 'ranged' ? 'selected' : ''}>Ranged</option>
                            <option value="none" ${item.AttackType === 'none' ? 'selected' : ''}>None</option>
                        </select>
                    </td>
                    <td class="actions-cell">
                        <button type="button" class="btn small-btn copy-row-btn" data-type="weapon" data-index="${index}" title="Copy Weapon Block">📋</button>
                        <button type="button" class="btn-duplicate-item small-btn" data-index="${index}" title="Duplicate Weapon">👯</button>
                        <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                    </td>
                </tr>
                <tr class="detail-row hidden" id="weapon-detail-${index}">
                    <td colspan="8">
                        <div class="detail-container">
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Projectile Speed (Optional)</label>
                                    <input type="number" class="weapon-speed" data-index="${index}" value="${item.ProjectileSpeed || 0}" min="0" step="any" />
                                </div>
                                <div class="form-group">
                                    <label>Visual Effect Model (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="weapon-visual" data-index="${index}" value="${item.VisualEffect || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="weapon-visual" data-index="${index}" data-file-types="gltf,glb,scn,tscn" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Attack Sound (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="weapon-sound" data-index="${index}" value="${item.AttackSound || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="weapon-sound" data-index="${index}" data-file-types="ogg,wav,mp3" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                                <div class="form-group">
                                    <label>Projectile Model Path (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="weapon-proj-model" data-index="${index}" value="${item.ProjectileModelPath || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="weapon-proj-model" data-index="${index}" data-file-types="gltf,glb,scn,tscn" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Impact Visual Effect (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="weapon-impact-visual" data-index="${index}" value="${item.ImpactVisualEffect || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="weapon-impact-visual" data-index="${index}" data-file-types="gltf,glb,scn,tscn" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                                <div class="form-group">
                                    <label>Impact Sound (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="weapon-impact-sound" data-index="${index}" value="${item.ImpactSound || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="weapon-impact-sound" data-index="${index}" data-file-types="ogg,wav,mp3" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </td>
                </tr>
            `;
        });

        tableContainer.innerHTML = `
            <table class="spreadsheet-table">
                <thead>
                    <tr>
                        <th style="width: 30px;"></th>
                        <th>Weapon ID</th>
                        <th>Name</th>
                        <th>Damage</th>
                        <th>Range</th>
                        <th>Cooldown</th>
                        <th>Attack Type</th>
                        <th style="width: 120px;">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    ${tbodyContent || '<tr><td colspan="8" class="no-slots-info">No weapons defined. Click "+ Add Custom Weapon" below.</td></tr>'}
                </tbody>
            </table>
        `;

        customWeaponsList.appendChild(tableContainer);
        setupNumericLockOnDynamicInputs();

        // Bind weapon listeners
        tableContainer.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                const targetId = list[idx].WeaponId;
                if (confirm(`Are you sure you want to delete custom weapon "${list[idx].Name || targetId}"?`)) {
                    pushToUndoStack();
                    cascadeDelete('weapon', targetId);
                    list.splice(idx, 1);
                    units.CustomWeapons = list;
                    saveChanges();
                    renderCustomWeapons();
                }
            });
        });

        tableContainer.querySelectorAll('.btn-duplicate-item').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                duplicateWeapon(idx);
            });
        });

        tableContainer.querySelectorAll('input, select').forEach(input => {
            input.addEventListener('change', e => {
                const idx = parseInt(e.target.dataset.index, 10);
                const target = e.target;
                const val = target.value;

                if (target.classList.contains('weapon-id')) {
                    const oldId = list[idx].WeaponId;
                    const newId = val.trim();
                    if (oldId !== newId) {
                        const dup = list.some((w, index) => index !== idx && w.WeaponId === newId);
                        if (newId && !dup) {
                            pushToUndoStack();
                            cascadeRename('weapon', oldId, newId);
                            list[idx].WeaponId = newId;
                        } else {
                            target.value = oldId;
                            return;
                        }
                    }
                }
                else if (target.classList.contains('weapon-name')) list[idx].Name = val;
                else if (target.classList.contains('weapon-damage')) list[idx].Damage = parseFloat(val) || 0;
                else if (target.classList.contains('weapon-range')) list[idx].Range = parseFloat(val) || 0;
                else if (target.classList.contains('weapon-cooldown')) list[idx].AttackCooldown = parseFloat(val) || 0;
                else if (target.classList.contains('weapon-type')) list[idx].AttackType = val;
                else if (target.classList.contains('weapon-speed')) list[idx].ProjectileSpeed = parseFloat(val) || 0;
                else if (target.classList.contains('weapon-visual')) list[idx].VisualEffect = val;
                else if (target.classList.contains('weapon-sound')) list[idx].AttackSound = val;
                else if (target.classList.contains('weapon-proj-model')) list[idx].ProjectileModelPath = val;
                else if (target.classList.contains('weapon-impact-visual')) list[idx].ImpactVisualEffect = val;
                else if (target.classList.contains('weapon-impact-sound')) list[idx].ImpactSound = val;

                units.CustomWeapons = list;
                saveChanges();
            });
        });
        updateAllThumbnails();
    }

    // Custom Abilities
    function renderCustomAbilities() {
        customAbilitiesList.innerHTML = '';
        const list = units.CustomAbilities || [];
        const validation = getValidationErrors();

        const tableContainer = document.createElement('div');
        tableContainer.className = 'spreadsheet-container';

        let tbodyContent = '';
        list.forEach((item, index) => {
            const abiErrors = validation.abilities[index] || {};
            const summonError = abiErrors['SummonedUnitId'];

            tbodyContent += `
                <tr class="main-row" data-index="${index}">
                    <td>
                        <button type="button" class="row-expand-btn" data-target="ability-detail-${index}">▶</button>
                    </td>
                    <td>
                        <input type="text" class="ability-id" data-index="${index}" value="${item.AbilityId || ''}" required />
                    </td>
                    <td>
                        <input type="text" class="ability-name" data-index="${index}" value="${item.Name || ''}" />
                    </td>
                    <td>
                        <select class="ability-type" data-index="${index}">
                            <option value="target_spell" ${item.AbilityType === 'target_spell' ? 'selected' : ''}>Target Spell</option>
                            <option value="instant_spell" ${item.AbilityType === 'instant_spell' ? 'selected' : ''}>Instant Spell</option>
                            <option value="passive" ${item.AbilityType === 'passive' ? 'selected' : ''}>Passive</option>
                        </select>
                    </td>
                    <td>
                        <input type="number" class="ability-mana" data-index="${index}" value="${item.ManaCost || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="ability-cooldown" data-index="${index}" value="${item.Cooldown || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="ability-range" data-index="${index}" value="${item.TargetRange || 0}" min="0" step="any" />
                    </td>
                    <td class="actions-cell">
                        <button type="button" class="btn small-btn copy-row-btn" data-type="ability" data-index="${index}" title="Copy Ability Block">📋</button>
                        <button type="button" class="btn-duplicate-item small-btn" data-index="${index}" title="Duplicate Ability">👯</button>
                        <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                    </td>
                </tr>
                <tr class="detail-row hidden" id="ability-detail-${index}">
                    <td colspan="8">
                        <div class="detail-container">
                            <div class="form-group">
                                <label>Description</label>
                                <textarea class="ability-desc" data-index="${index}" rows="2">${item.Description || ''}</textarea>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Visual Effect Model (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="ability-visual" data-index="${index}" value="${item.VisualEffect || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="ability-visual" data-index="${index}" data-file-types="gltf,glb,scn,tscn" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                                <div class="form-group">
                                    <label>Cast Sound (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="ability-sound" data-index="${index}" value="${item.CastSound || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="ability-sound" data-index="${index}" data-file-types="ogg,wav,mp3" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Icon Path (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="ability-icon" data-index="${index}" value="${item.IconPath || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="ability-icon" data-index="${index}" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                                <div class="form-group">
                                    <label>Area of Effect Radius</label>
                                    <input type="number" class="ability-aoe" data-index="${index}" value="${item.AreaOfEffectRadius || 0}" min="0" step="any" />
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Direct Damage</label>
                                    <input type="number" class="ability-damage" data-index="${index}" value="${item.Damage || 0}" min="0" step="any" />
                                </div>
                                <div class="form-group">
                                    <label>Direct Healing</label>
                                    <input type="number" class="ability-healing" data-index="${index}" value="${item.Healing || 0}" min="0" step="any" />
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Summoned Unit ID (Optional)</label>
                                    <input type="text" class="ability-summon-id${summonError ? ' input-warning' : ''}" list="suggest-units" data-index="${index}" value="${item.SummonedUnitId || ''}" title="${summonError || ''}" />
                                    ${summonError ? `<span class="validation-warning-text">${summonError}</span>` : ''}
                                </div>
                                <div class="form-group">
                                    <label>Summon Count</label>
                                    <input type="number" class="ability-summon-count" data-index="${index}" value="${item.SummonCount || 1}" min="1" step="1" />
                                </div>
                                <div class="form-group">
                                    <label>Summon Duration (Seconds)</label>
                                    <input type="number" class="ability-summon-duration" data-index="${index}" value="${item.SummonDuration || 0}" min="0" step="any" />
                                </div>
                            </div>

                            <!-- INLINE NESTED TABLE: Applied Status Effects -->
                            <div class="sub-table-group">
                                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                                    <label style="font-weight: 600;">Applied Status Effects</label>
                                    <div style="display: flex; gap: 4px;">
                                        <button type="button" class="btn small-btn copy-subtable-btn" data-type="AppliedStatusEffects" data-parent-index="${index}" title="Copy Status Effects block">📋 Copy Block</button>
                                        <button type="button" class="btn small-btn paste-subtable-btn" data-type="AppliedStatusEffects" data-parent-index="${index}" title="Paste Status Effects block">📥 Paste Block</button>
                                        <button type="button" class="btn secondary-btn small-btn add-subitem-btn" data-type="AppliedStatusEffects" data-parent-index="${index}">+ Add Effect</button>
                                    </div>
                                </div>
                                <div class="sub-table-container">
                                    <table class="sub-table">
                                        <thead>
                                            <tr>
                                                <th>Status Effect ID</th>
                                                <th style="width: 40px;"></th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            ${(item.AppliedStatusEffects || []).map((effect, subIdx) => `
                                                <tr>
                                                    <td>
                                                        <input type="text" class="subitem-input" data-type="AppliedStatusEffects" data-parent-index="${index}" data-sub-index="${subIdx}" value="${effect || ''}" placeholder="e.g. slow" />
                                                    </td>
                                                    <td>
                                                        <button type="button" class="btn-delete-subitem btn-delete" data-type="AppliedStatusEffects" data-parent-index="${index}" data-sub-index="${subIdx}">&times;</button>
                                                    </td>
                                                </tr>
                                            `).join('')}
                                            ${!(item.AppliedStatusEffects && item.AppliedStatusEffects.length) ? `<tr><td colspan="2" class="no-slots-info">No status effects.</td></tr>` : ''}
                                        </tbody>
                                    </table>
                                </div>
                            </div>
                        </div>
                    </td>
                </tr>
            `;
        });

        tableContainer.innerHTML = `
            <table class="spreadsheet-table">
                <thead>
                    <tr>
                        <th style="width: 30px;"></th>
                        <th>Ability ID</th>
                        <th>Name</th>
                        <th>Type</th>
                        <th>Mana Cost</th>
                        <th>Cooldown</th>
                        <th>Range</th>
                        <th style="width: 120px;">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    ${tbodyContent || '<tr><td colspan="8" class="no-slots-info">No abilities defined. Click "+ Add Custom Ability" below.</td></tr>'}
                </tbody>
            </table>
        `;

        customAbilitiesList.appendChild(tableContainer);
        setupNumericLockOnDynamicInputs();

        // Bind ability listeners
        tableContainer.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                const targetId = list[idx].AbilityId;
                if (confirm(`Are you sure you want to delete custom ability "${list[idx].Name || targetId}"?`)) {
                    pushToUndoStack();
                    cascadeDelete('ability', targetId);
                    list.splice(idx, 1);
                    units.CustomAbilities = list;
                    saveChanges();
                    renderCustomAbilities();
                }
            });
        });

        tableContainer.querySelectorAll('.btn-duplicate-item').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                duplicateAbility(idx);
            });
        });

        tableContainer.querySelectorAll('input, select, textarea').forEach(input => {
            if (input.classList.contains('subitem-input')) return; // handled separately
            input.addEventListener('change', e => {
                const idx = parseInt(e.target.dataset.index, 10);
                const target = e.target;
                const val = target.value;

                if (target.classList.contains('ability-id')) {
                    const oldId = list[idx].AbilityId;
                    const newId = val.trim();
                    if (oldId !== newId) {
                        const dup = list.some((a, index) => index !== idx && a.AbilityId === newId);
                        if (newId && !dup) {
                            pushToUndoStack();
                            cascadeRename('ability', oldId, newId);
                            list[idx].AbilityId = newId;
                        } else {
                            target.value = oldId;
                            return;
                        }
                    }
                }
                else if (target.classList.contains('ability-name')) list[idx].Name = val;
                else if (target.classList.contains('ability-type')) list[idx].AbilityType = val;
                else if (target.classList.contains('ability-mana')) list[idx].ManaCost = parseFloat(val) || 0;
                else if (target.classList.contains('ability-cooldown')) list[idx].Cooldown = parseFloat(val) || 0;
                else if (target.classList.contains('ability-range')) list[idx].TargetRange = parseFloat(val) || 0;
                else if (target.classList.contains('ability-desc')) list[idx].Description = val;
                else if (target.classList.contains('ability-visual')) list[idx].VisualEffect = val;
                else if (target.classList.contains('ability-sound')) list[idx].CastSound = val;
                else if (target.classList.contains('ability-icon')) list[idx].IconPath = val;
                else if (target.classList.contains('ability-aoe')) list[idx].AreaOfEffectRadius = parseFloat(val) || 0;
                else if (target.classList.contains('ability-damage')) list[idx].Damage = parseFloat(val) || 0;
                else if (target.classList.contains('ability-healing')) list[idx].Healing = parseFloat(val) || 0;
                else if (target.classList.contains('ability-summon-id')) list[idx].SummonedUnitId = val;
                else if (target.classList.contains('ability-summon-count')) list[idx].SummonCount = parseInt(val, 10) || 1;
                else if (target.classList.contains('ability-summon-duration')) list[idx].SummonDuration = parseFloat(val) || 0;

                units.CustomAbilities = list;
                saveChanges();
            });
        });
        updateAllThumbnails();
    }

    // Custom Upgrades
    function renderCustomUpgrades() {
        customUpgradesList.innerHTML = '';
        const list = units.CustomUpgrades || [];
        const validation = getValidationErrors();

        const tableContainer = document.createElement('div');
        tableContainer.className = 'spreadsheet-container';

        let tbodyContent = '';
        list.forEach((item, index) => {
            const upgErrors = validation.upgrades[index] || {};
            const affectedErrors = Object.keys(upgErrors)
                .filter(k => k.startsWith('AffectedUnitIds_'))
                .map(k => upgErrors[k])
                .join(' ');

            tbodyContent += `
                <tr class="main-row" data-index="${index}">
                    <td>
                        <button type="button" class="row-expand-btn" data-target="upgrade-detail-${index}">▶</button>
                    </td>
                    <td>
                        <input type="text" class="upgrade-id" data-index="${index}" value="${item.UpgradeId || ''}" required />
                    </td>
                    <td>
                        <input type="text" class="upgrade-name" data-index="${index}" value="${item.Name || ''}" />
                    </td>
                    <td>
                        <input type="number" class="upgrade-gold" data-index="${index}" value="${item.CostGold || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="upgrade-wood" data-index="${index}" value="${item.CostWood || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="upgrade-stone" data-index="${index}" value="${item.CostStone || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="upgrade-time" data-index="${index}" value="${item.ResearchTime || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="number" class="upgrade-levels" data-index="${index}" value="${item.MaxLevel || 1}" min="1" step="1" />
                    </td>
                    <td class="actions-cell">
                        <button type="button" class="btn small-btn copy-row-btn" data-type="upgrade" data-index="${index}" title="Copy Upgrade Block">📋</button>
                        <button type="button" class="btn-duplicate-item small-btn" data-index="${index}" title="Duplicate Upgrade">👯</button>
                        <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                    </td>
                </tr>
                <tr class="detail-row hidden" id="upgrade-detail-${index}">
                    <td colspan="9">
                        <div class="detail-container">
                            <div class="form-group">
                                <label>Description</label>
                                <textarea class="upgrade-desc" data-index="${index}" rows="2">${item.Description || ''}</textarea>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Upgrade Requirement (Prerequisite)</label>
                                    <input type="text" class="upgrade-req" list="suggest-upgrades" data-index="${index}" value="${item.Requirement || ''}" placeholder="e.g. blacksmith" />
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Max HP Bonus per Level</label>
                                    <input type="number" class="upgrade-hp-bonus" data-index="${index}" value="${item.MaxHpBonus || 0}" step="any" />
                                </div>
                                <div class="form-group">
                                    <label>Damage Bonus per Level</label>
                                    <input type="number" class="upgrade-dmg-bonus" data-index="${index}" value="${item.DamageBonus || 0}" step="any" />
                                </div>
                                <div class="form-group">
                                    <label>Armor Bonus per Level</label>
                                    <input type="number" class="upgrade-arm-bonus" data-index="${index}" value="${item.ArmorBonus || 0}" step="any" />
                                </div>
                                <div class="form-group">
                                    <label>Speed Bonus per Level</label>
                                    <input type="number" class="upgrade-spd-bonus" data-index="${index}" value="${item.SpeedBonus || 0}" step="any" />
                                </div>
                            </div>

                            <!-- INLINE NESTED TABLE: Affected Unit IDs -->
                            <div class="sub-table-group">
                                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                                    <label style="font-weight: 600;">Affected Unit IDs</label>
                                    <div style="display: flex; gap: 4px;">
                                        <button type="button" class="btn small-btn copy-subtable-btn" data-type="AffectedUnitIds" data-parent-index="${index}" title="Copy Affected Units block">📋 Copy Block</button>
                                        <button type="button" class="btn small-btn paste-subtable-btn" data-type="AffectedUnitIds" data-parent-index="${index}" title="Paste Affected Units block">📥 Paste Block</button>
                                        <button type="button" class="btn secondary-btn small-btn add-subitem-btn" data-type="AffectedUnitIds" data-parent-index="${index}">+ Add Unit</button>
                                    </div>
                                </div>
                                <div class="sub-table-container">
                                    <table class="sub-table">
                                        <thead>
                                            <tr>
                                                <th>Unit ID</th>
                                                <th style="width: 40px;"></th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            ${(item.AffectedUnitIds || []).map((uId, subIdx) => `
                                                <tr>
                                                    <td>
                                                        <input type="text" class="subitem-input" data-type="AffectedUnitIds" data-parent-index="${index}" data-sub-index="${subIdx}" value="${uId || ''}" list="suggest-units" placeholder="e.g. soldier" />
                                                    </td>
                                                    <td>
                                                        <button type="button" class="btn-delete-subitem btn-delete" data-type="AffectedUnitIds" data-parent-index="${index}" data-sub-index="${subIdx}">&times;</button>
                                                    </td>
                                                </tr>
                                            `).join('')}
                                            ${!(item.AffectedUnitIds && item.AffectedUnitIds.length) ? `<tr><td colspan="2" class="no-slots-info">No units affected.</td></tr>` : ''}
                                        </tbody>
                                    </table>
                                </div>
                                ${affectedErrors ? `<span class="validation-warning-text">${affectedErrors}</span>` : ''}
                            </div>
                        </div>
                    </td>
                </tr>
            `;
        });

        tableContainer.innerHTML = `
            <table class="spreadsheet-table">
                <thead>
                    <tr>
                        <th style="width: 30px;"></th>
                        <th>Upgrade ID</th>
                        <th>Name</th>
                        <th>Gold</th>
                        <th>Wood</th>
                        <th>Stone</th>
                        <th>Time</th>
                        <th>Max Lvl</th>
                        <th style="width: 120px;">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    ${tbodyContent || '<tr><td colspan="9" class="no-slots-info">No upgrades defined. Click "+ Add Custom Upgrade" below.</td></tr>'}
                </tbody>
            </table>
        `;

        customUpgradesList.appendChild(tableContainer);
        setupNumericLockOnDynamicInputs();

        // Bind upgrade listeners
        tableContainer.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                const targetId = list[idx].UpgradeId;
                if (confirm(`Are you sure you want to delete custom upgrade "${list[idx].Name || targetId}"?`)) {
                    pushToUndoStack();
                    cascadeDelete('upgrade', targetId);
                    list.splice(idx, 1);
                    units.CustomUpgrades = list;
                    saveChanges();
                    renderCustomUpgrades();
                }
            });
        });

        tableContainer.querySelectorAll('.btn-duplicate-item').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                duplicateUpgrade(idx);
            });
        });

        tableContainer.querySelectorAll('input, textarea').forEach(input => {
            if (input.classList.contains('subitem-input')) return;
            input.addEventListener('change', e => {
                const idx = parseInt(e.target.dataset.index, 10);
                const target = e.target;
                const val = target.value;

                if (target.classList.contains('upgrade-id')) {
                    const oldId = list[idx].UpgradeId;
                    const newId = val.trim();
                    if (oldId !== newId) {
                        const dup = list.some((u, index) => index !== idx && u.UpgradeId === newId);
                        if (newId && !dup) {
                            pushToUndoStack();
                            cascadeRename('upgrade', oldId, newId);
                            list[idx].UpgradeId = newId;
                        } else {
                            target.value = oldId;
                            return;
                        }
                    }
                }
                else if (target.classList.contains('upgrade-name')) list[idx].Name = val;
                else if (target.classList.contains('upgrade-gold')) list[idx].CostGold = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-wood')) list[idx].CostWood = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-stone')) list[idx].CostStone = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-time')) list[idx].ResearchTime = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-levels')) list[idx].MaxLevel = parseInt(val, 10) || 1;
                else if (target.classList.contains('upgrade-desc')) list[idx].Description = val;
                else if (target.classList.contains('upgrade-req')) list[idx].Requirement = val;
                else if (target.classList.contains('upgrade-hp-bonus')) list[idx].MaxHpBonus = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-dmg-bonus')) list[idx].DamageBonus = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-arm-bonus')) list[idx].ArmorBonus = parseFloat(val) || 0;
                else if (target.classList.contains('upgrade-spd-bonus')) list[idx].SpeedBonus = parseFloat(val) || 0;

                units.CustomUpgrades = list;
                saveChanges();
            });
        });
    }

    // Custom Items
    function renderCustomItems() {
        customItemsList.innerHTML = '';
        const list = units.CustomItems || [];
        const validation = getValidationErrors();

        const tableContainer = document.createElement('div');
        tableContainer.className = 'spreadsheet-container';

        let tbodyContent = '';
        list.forEach((item, index) => {
            const itemErrors = validation.items[index] || {};
            const abilityError = itemErrors['UseAbility'];

            tbodyContent += `
                <tr class="main-row" data-index="${index}">
                    <td>
                        <button type="button" class="row-expand-btn" data-target="item-detail-${index}">▶</button>
                    </td>
                    <td>
                        <input type="text" class="item-id" data-index="${index}" value="${item.ItemId || ''}" required />
                    </td>
                    <td>
                        <input type="text" class="item-name" data-index="${index}" value="${item.Name || ''}" />
                    </td>
                    <td>
                        <select class="item-class" data-index="${index}">
                            <option value="consumable" ${item.ItemClass === 'consumable' ? 'selected' : ''}>Consumable</option>
                            <option value="equipment" ${item.ItemClass === 'equipment' ? 'selected' : ''}>Equipment</option>
                            <option value="quest" ${item.ItemClass === 'quest' ? 'selected' : ''}>Quest Item</option>
                        </select>
                    </td>
                    <td>
                        <input type="number" class="item-gold" data-index="${index}" value="${item.CostGold || 0}" min="0" step="any" />
                    </td>
                    <td>
                        <input type="text" class="item-ability${abilityError ? ' input-warning' : ''}" list="suggest-abilities" data-index="${index}" value="${item.UseAbility || ''}" title="${abilityError || ''}" />
                    </td>
                    <td>
                        <input type="number" class="item-level" data-index="${index}" value="${item.ItemLevel || 0}" min="0" step="1" />
                    </td>
                    <td class="actions-cell">
                        <button type="button" class="btn small-btn copy-row-btn" data-type="item" data-index="${index}" title="Copy Item Block">📋</button>
                        <button type="button" class="btn-duplicate-item small-btn" data-index="${index}" title="Duplicate Item">👯</button>
                        <button type="button" class="btn-delete" data-index="${index}">&times;</button>
                    </td>
                </tr>
                <tr class="detail-row hidden" id="item-detail-${index}">
                    <td colspan="8">
                        <div class="detail-container">
                            <div class="form-group">
                                <label>Description</label>
                                <textarea class="item-desc" data-index="${index}" rows="2">${item.Description || ''}</textarea>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Starting Charges</label>
                                    <input type="number" class="item-charges" data-index="${index}" value="${item.ChargeCount || 0}" min="0" step="1" />
                                </div>
                                <div class="form-group">
                                    <label>Cooldown Group Link</label>
                                    <input type="text" class="item-cooldown-link" data-index="${index}" value="${item.CooldownLink || ''}" />
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group checkbox-group">
                                    <input type="checkbox" class="item-candrop" id="item-${index}-candrop" data-index="${index}" ${item.CanDrop ? 'checked' : ''} />
                                    <label for="item-${index}-candrop">Can drop from inventory</label>
                                </div>
                                <div class="form-group">
                                    <label>Icon Path (Optional)</label>
                                    <div class="input-with-browse">
                                        <input type="text" class="item-icon" data-index="${index}" value="${item.IconPath || ''}" />
                                        <button type="button" class="btn browse-btn" data-class="item-icon" data-index="${index}" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">📁</button>
                                        <button type="button" class="btn clear-btn" title="Clear path">❌</button>
                                    </div>
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group checkbox-group">
                                    <input type="checkbox" class="item-iscontainer" id="item-${index}-iscontainer" data-index="${index}" ${item.IsContainer ? 'checked' : ''} />
                                    <label for="item-${index}-iscontainer">Is container/bag</label>
                                </div>
                                <div class="form-group">
                                    <label>Container Capacity</label>
                                    <input type="number" class="item-containersize" data-index="${index}" value="${item.ContainerSize || 0}" min="0" step="1" />
                                </div>
                            </div>
                            <div class="form-row">
                                <div class="form-group">
                                    <label>Purchase Tech Prerequisite (Optional)</label>
                                    <input type="text" class="item-req" list="suggest-upgrades" data-index="${index}" value="${item.Requirements || ''}" />
                                </div>
                            </div>

                            <!-- INLINE NESTED TABLE: Passive Status Effects -->
                            <div class="sub-table-group">
                                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                                    <label style="font-weight: 600;">Passive Status Effects</label>
                                    <div style="display: flex; gap: 4px;">
                                        <button type="button" class="btn small-btn copy-subtable-btn" data-type="PassiveStatusEffects" data-parent-index="${index}" title="Copy Passive Effects block">📋 Copy Block</button>
                                        <button type="button" class="btn small-btn paste-subtable-btn" data-type="PassiveStatusEffects" data-parent-index="${index}" title="Paste Passive Effects block">📥 Paste Block</button>
                                        <button type="button" class="btn secondary-btn small-btn add-subitem-btn" data-type="PassiveStatusEffects" data-parent-index="${index}">+ Add Passive</button>
                                    </div>
                                </div>
                                <div class="sub-table-container">
                                    <table class="sub-table">
                                        <thead>
                                            <tr>
                                                <th>Passive Buff ID</th>
                                                <th style="width: 40px;"></th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            ${(item.PassiveStatusEffects || []).map((effect, subIdx) => `
                                                <tr>
                                                    <td>
                                                        <input type="text" class="subitem-input" data-type="PassiveStatusEffects" data-parent-index="${index}" data-sub-index="${subIdx}" value="${effect || ''}" placeholder="e.g. speed_boost" />
                                                    </td>
                                                    <td>
                                                        <button type="button" class="btn-delete-subitem btn-delete" data-type="PassiveStatusEffects" data-parent-index="${index}" data-sub-index="${subIdx}">&times;</button>
                                                    </td>
                                                </tr>
                                            `).join('')}
                                            ${!(item.PassiveStatusEffects && item.PassiveStatusEffects.length) ? `<tr><td colspan="2" class="no-slots-info">No passive effects.</td></tr>` : ''}
                                        </tbody>
                                    </table>
                                </div>
                            </div>

                            <!-- INLINE NESTED TABLE: Granted Weapons -->
                            <div class="sub-table-group">
                                <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px;">
                                    <label style="font-weight: 600;">Granted Weapons</label>
                                    <div style="display: flex; gap: 4px;">
                                        <button type="button" class="btn small-btn copy-subtable-btn" data-type="GrantedWeapons" data-parent-index="${index}" title="Copy Granted Weapons block">📋 Copy Block</button>
                                        <button type="button" class="btn small-btn paste-subtable-btn" data-type="GrantedWeapons" data-parent-index="${index}" title="Paste Granted Weapons block">📥 Paste Block</button>
                                        <button type="button" class="btn secondary-btn small-btn add-subitem-btn" data-type="GrantedWeapons" data-parent-index="${index}">+ Add Weapon</button>
                                    </div>
                                </div>
                                <div class="sub-table-container">
                                    <table class="sub-table">
                                        <thead>
                                            <tr>
                                                <th>Weapon ID</th>
                                                <th style="width: 40px;"></th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            ${(item.GrantedWeapons || []).map((wId, subIdx) => `
                                                <tr>
                                                    <td>
                                                        <input type="text" class="subitem-input" data-type="GrantedWeapons" data-parent-index="${index}" data-sub-index="${subIdx}" value="${wId || ''}" list="suggest-weapons" placeholder="e.g. frost_sword" />
                                                    </td>
                                                    <td>
                                                        <button type="button" class="btn-delete-subitem btn-delete" data-type="GrantedWeapons" data-parent-index="${index}" data-sub-index="${subIdx}">&times;</button>
                                                    </td>
                                                </tr>
                                            `).join('')}
                                            ${!(item.GrantedWeapons && item.GrantedWeapons.length) ? `<tr><td colspan="2" class="no-slots-info">No weapons granted.</td></tr>` : ''}
                                        </tbody>
                                    </table>
                                </div>
                            </div>
                        </div>
                    </td>
                </tr>
            `;
        });

        tableContainer.innerHTML = `
            <table class="spreadsheet-table">
                <thead>
                    <tr>
                        <th style="width: 30px;"></th>
                        <th>Item ID</th>
                        <th>Name</th>
                        <th>Item Class</th>
                        <th>Gold Cost</th>
                        <th>Use Ability</th>
                        <th>Item Level</th>
                        <th style="width: 120px;">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    ${tbodyContent || '<tr><td colspan="8" class="no-slots-info">No items defined. Click "+ Add Custom Item" below.</td></tr>'}
                </tbody>
            </table>
        `;

        customItemsList.appendChild(tableContainer);
        setupNumericLockOnDynamicInputs();

        // Bind item listeners
        tableContainer.querySelectorAll('.btn-delete').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                const targetId = list[idx].ItemId;
                if (confirm(`Are you sure you want to delete custom item "${list[idx].Name || targetId}"?`)) {
                    pushToUndoStack();
                    cascadeDelete('item', targetId);
                    list.splice(idx, 1);
                    units.CustomItems = list;
                    saveChanges();
                    renderCustomItems();
                }
            });
        });

        tableContainer.querySelectorAll('.btn-duplicate-item').forEach(btn => {
            btn.addEventListener('click', () => {
                const idx = parseInt(btn.dataset.index, 10);
                duplicateItem(idx);
            });
        });

        tableContainer.querySelectorAll('input, select, textarea').forEach(input => {
            if (input.classList.contains('subitem-input')) return;
            input.addEventListener('change', e => {
                const idx = parseInt(e.target.dataset.index, 10);
                const target = e.target;
                const val = target.type === 'checkbox' ? target.checked : target.value;

                if (target.classList.contains('item-id')) {
                    const oldId = list[idx].ItemId;
                    const newId = val.trim();
                    if (oldId !== newId) {
                        const dup = list.some((i, index) => index !== idx && i.ItemId === newId);
                        if (newId && !dup) {
                            pushToUndoStack();
                            cascadeRename('item', oldId, newId);
                            list[idx].ItemId = newId;
                        } else {
                            target.value = oldId;
                            return;
                        }
                    }
                }
                else if (target.classList.contains('item-name')) list[idx].Name = val;
                else if (target.classList.contains('item-class')) list[idx].ItemClass = val;
                else if (target.classList.contains('item-gold')) list[idx].CostGold = parseFloat(val) || 0;
                else if (target.classList.contains('item-ability')) list[idx].UseAbility = val;
                else if (target.classList.contains('item-level')) list[idx].ItemLevel = parseInt(val, 10) || 0;
                else if (target.classList.contains('item-desc')) list[idx].Description = val;
                else if (target.classList.contains('item-charges')) list[idx].ChargeCount = parseInt(val, 10) || 0;
                else if (target.classList.contains('item-cooldown-link')) list[idx].CooldownLink = val;
                else if (target.classList.contains('item-candrop')) list[idx].CanDrop = val;
                else if (target.classList.contains('item-icon')) list[idx].IconPath = val;
                else if (target.classList.contains('item-iscontainer')) list[idx].IsContainer = val;
                else if (target.classList.contains('item-containersize')) list[idx].ContainerSize = parseInt(val, 10) || 0;
                else if (target.classList.contains('item-req')) list[idx].Requirements = val;

                units.CustomItems = list;
                saveChanges();
            });
        });
        updateAllThumbnails();
    }

    // --- INLINE SUB-TABLE HANDLERS (AppliedStatusEffects, AffectedUnitIds, PassiveStatusEffects, GrantedWeapons) ---
    function getSubitemArray(type, parentIndex) {
        if (type === 'AppliedStatusEffects') {
            const item = units.CustomAbilities[parentIndex];
            if (!item.AppliedStatusEffects) item.AppliedStatusEffects = [];
            return item.AppliedStatusEffects;
        } else if (type === 'AffectedUnitIds') {
            const item = units.CustomUpgrades[parentIndex];
            if (!item.AffectedUnitIds) item.AffectedUnitIds = [];
            return item.AffectedUnitIds;
        } else if (type === 'PassiveStatusEffects') {
            const item = units.CustomItems[parentIndex];
            if (!item.PassiveStatusEffects) item.PassiveStatusEffects = [];
            return item.PassiveStatusEffects;
        } else if (type === 'GrantedWeapons') {
            const item = units.CustomItems[parentIndex];
            if (!item.GrantedWeapons) item.GrantedWeapons = [];
            return item.GrantedWeapons;
        }
        return null;
    }

    function saveSubitemArray(type, parentIndex, arr) {
        if (type === 'AppliedStatusEffects') {
            units.CustomAbilities[parentIndex].AppliedStatusEffects = arr;
            renderCustomAbilities();
        } else if (type === 'AffectedUnitIds') {
            units.CustomUpgrades[parentIndex].AffectedUnitIds = arr;
            renderCustomUpgrades();
        } else if (type === 'PassiveStatusEffects') {
            units.CustomItems[parentIndex].PassiveStatusEffects = arr;
            renderCustomItems();
        } else if (type === 'GrantedWeapons') {
            units.CustomItems[parentIndex].GrantedWeapons = arr;
            renderCustomItems();
        }
        saveChanges();
    }

    // Delegate click and inputs for nested sub-tables
    document.addEventListener('click', e => {
        if (isLocked) return;

        // Add sub-item button
        const addBtn = e.target.closest('.add-subitem-btn');
        if (addBtn) {
            const type = addBtn.dataset.type;
            const parentIndex = parseInt(addBtn.dataset.parentIndex, 10);
            const arr = getSubitemArray(type, parentIndex);
            if (arr) {
                pushToUndoStack();
                arr.push('');
                saveSubitemArray(type, parentIndex, arr);
            }
            return;
        }

        // Delete sub-item button
        const delBtn = e.target.closest('.btn-delete-subitem');
        if (delBtn) {
            const type = delBtn.dataset.type;
            const parentIndex = parseInt(delBtn.dataset.parentIndex, 10);
            const subIdx = parseInt(delBtn.dataset.subIndex, 10);
            const arr = getSubitemArray(type, parentIndex);
            if (arr && arr[subIdx] !== undefined) {
                pushToUndoStack();
                arr.splice(subIdx, 1);
                saveSubitemArray(type, parentIndex, arr);
            }
            return;
        }

        // Copy subtable block
        const copySubBtn = e.target.closest('.copy-subtable-btn');
        if (copySubBtn) {
            const type = copySubBtn.dataset.type;
            const parentIndex = parseInt(copySubBtn.dataset.parentIndex, 10);
            const arr = getSubitemArray(type, parentIndex);
            if (arr) {
                navigator.clipboard.writeText(JSON.stringify({
                    "$realm_editor_type": "subitem-block",
                    "dataType": type,
                    "data": arr
                }));
            }
            return;
        }

        // Paste subtable block
        const pasteSubBtn = e.target.closest('.paste-subtable-btn');
        if (pasteSubBtn) {
            const type = pasteSubBtn.dataset.type;
            const parentIndex = parseInt(pasteSubBtn.dataset.parentIndex, 10);
            navigator.clipboard.readText().then(text => {
                try {
                    const parsed = JSON.parse(text);
                    if (parsed && parsed.$realm_editor_type === 'subitem-block' && Array.isArray(parsed.data)) {
                        pushToUndoStack();
                        saveSubitemArray(type, parentIndex, parsed.data);
                    }
                } catch (err) {
                    console.error("Failed to paste subtable block:", err);
                }
            });
            return;
        }
    });

    document.addEventListener('change', e => {
        const input = e.target.closest('.subitem-input');
        if (input) {
            const type = input.dataset.type;
            const parentIndex = parseInt(input.dataset.parentIndex, 10);
            const subIdx = parseInt(input.dataset.subIndex, 10);
            const arr = getSubitemArray(type, parentIndex);
            if (arr && arr[subIdx] !== undefined) {
                pushToUndoStack();
                arr[subIdx] = input.value.trim();
                saveSubitemArray(type, parentIndex, arr);
            }
        }
    });

    // --- INCREMENTAL ID GENERATOR (SQL IDENTITY STYLE) ---
    function generateNextId(prefix, existingIds) {
        let index = 1;
        while (true) {
            let indexStr = String(index).padStart(3, '0');
            let id = `${prefix}_${indexStr}`;
            if (!existingIds.has(id)) {
                return id;
            }
            index++;
        }
    }

    function getExistingUnitIds() {
        const customUnitsList = getCustomUnits();
        return new Set(customUnitsList.map(u => u.UnitId).filter(Boolean));
    }

    // --- UNIT COPY / PASTE SYSTEM CLIPBOARD ---
    if (copyUnitBtn) {
        copyUnitBtn.addEventListener('click', () => {
            const sourceUnit = getUnitById(selectedUnitId);
            if (!selectedUnitId || !sourceUnit) return;
            navigator.clipboard.writeText(JSON.stringify({
                "$realm_editor_type": "unit",
                "data": sourceUnit
            })).then(() => {
                const oldText = copyUnitBtn.innerHTML;
                copyUnitBtn.innerHTML = '✅ Copied!';
                setTimeout(() => copyUnitBtn.innerHTML = oldText, 1500);
            });
        });
    }

    if (pasteUnitBtn) {
        pasteUnitBtn.addEventListener('click', () => {
            if (isLocked) return;
            navigator.clipboard.readText().then(text => {
                try {
                    const parsed = JSON.parse(text);
                    if (parsed && parsed.$realm_editor_type === 'unit' && parsed.data) {
                        pushToUndoStack();

                        // Schema Keys to sanitize (strip map-specific overrides or non-schema fields)
                        const schemaKeys = [
                            "UnitId", "Name", "Description", "MaxHp", "Damage", "Range", "Armor", "Speed",
                            "AttackCooldown", "ScanRadius", "CostGold", "CostWood", "CostStone", "ProductionTime",
                            "PopCost", "AttackType", "ArmorType", "GoldBounty", "ModelPath", "BuildOptions",
                            "IsHero", "Abilities", "XpBounty", "Weapons", "StartingItems", "Upgrades",
                            "PathingType", "StatusEffects", "SoundEvents", "PortraitModelPath"
                        ];
                        const sanitized = {};
                        schemaKeys.forEach(k => {
                            if (parsed.data[k] !== undefined) {
                                sanitized[k] = parsed.data[k];
                            }
                        });

                        // Compute next unique ID
                        const nextId = generateNextId('Unit', getExistingUnitIds());
                        sanitized.UnitId = nextId;
                        sanitized.Name = `${sanitized.Name || 'Pasted Unit'} (Copy)`;

                        const domain = getActiveDomain();
                        const targetArr = domain === 'buildings' ? (units.CustomBuildings = units.CustomBuildings || []) :
                                          domain === 'resources' ? (units.CustomResources = units.CustomResources || []) :
                                          domain === 'props' ? (units.CustomProps = units.CustomProps || []) :
                                          (units.CustomUnits = units.CustomUnits || []);

                        targetArr.push(sanitized);
                        selectUnit(nextId);
                        saveChanges();
                    }
                } catch (e) {
                    console.error("Paste Unit failed:", e);
                }
            });
        });
    }

    // --- ROW COPY / PASTE SYSTEM CLIPBOARD ---
    document.addEventListener('click', e => {
        const copyRowBtn = e.target.closest('.copy-row-btn');
        if (copyRowBtn) {
            const type = copyRowBtn.dataset.type;
            const index = parseInt(copyRowBtn.dataset.index, 10);
            let itemData = null;
            if (type === 'weapon') itemData = units.CustomWeapons[index];
            else if (type === 'ability') itemData = units.CustomAbilities[index];
            else if (type === 'upgrade') itemData = units.CustomUpgrades[index];
            else if (type === 'item') itemData = units.CustomItems[index];

            if (itemData) {
                navigator.clipboard.writeText(JSON.stringify({
                    "$realm_editor_type": type,
                    "data": itemData
                })).then(() => {
                    const oldText = copyRowBtn.innerHTML;
                    copyRowBtn.innerHTML = '✅';
                    setTimeout(() => copyRowBtn.innerHTML = oldText, 1500);
                });
            }
        }
    });

    function pasteRowData(type, parentList, idField, prefix) {
        if (isLocked) return;
        navigator.clipboard.readText().then(text => {
            try {
                const parsed = JSON.parse(text);
                if (parsed && parsed.$realm_editor_type === type && parsed.data) {
                    pushToUndoStack();

                    // Duplicate/sanitize item
                    const sanitized = JSON.parse(JSON.stringify(parsed.data));
                    const existingIds = new Set(parentList.map(w => w[idField]));

                    // Generate next unique incremented ID
                    const nextId = generateNextId(prefix, existingIds);
                    sanitized[idField] = nextId;
                    if (sanitized.Name) sanitized.Name = `${sanitized.Name} (Copy)`;

                    parentList.push(sanitized);
                    saveChanges();
                    if (type === 'weapon') renderCustomWeapons();
                    else if (type === 'ability') renderCustomAbilities();
                    else if (type === 'upgrade') renderCustomUpgrades();
                    else if (type === 'item') renderCustomItems();
                }
            } catch (err) {
                console.error("Paste row failed:", err);
            }
        });
    }

    if (pasteCustomWeaponBtn) {
        pasteCustomWeaponBtn.addEventListener('click', () => {
            if (!units.CustomWeapons) units.CustomWeapons = [];
            pasteRowData('weapon', units.CustomWeapons, 'WeaponId', 'Weapon');
        });
    }
    if (pasteCustomAbilityBtn) {
        pasteCustomAbilityBtn.addEventListener('click', () => {
            if (!units.CustomAbilities) units.CustomAbilities = [];
            pasteRowData('ability', units.CustomAbilities, 'AbilityId', 'Ability');
        });
    }
    if (pasteCustomUpgradeBtn) {
        pasteCustomUpgradeBtn.addEventListener('click', () => {
            if (!units.CustomUpgrades) units.CustomUpgrades = [];
            pasteRowData('upgrade', units.CustomUpgrades, 'UpgradeId', 'Upgrade');
        });
    }
    if (pasteCustomItemBtn) {
        pasteCustomItemBtn.addEventListener('click', () => {
            if (!units.CustomItems) units.CustomItems = [];
            pasteRowData('item', units.CustomItems, 'ItemId', 'Item');
        });
    }

    // --- COMPONENT LEVEL COPY/PASTE FOR UNIT FORM ---
    function copyUnitComponent(componentKey) {
        const unit = getUnitById(selectedUnitId);
        if (!selectedUnitId || !unit) return;
        const arr = unit[componentKey] || [];
        navigator.clipboard.writeText(JSON.stringify({
            "$realm_editor_type": "unit-component-block",
            "componentType": componentKey,
            "data": arr
        }));
    }

    function pasteUnitComponent(componentKey) {
        if (isLocked) return;
        const unit = getUnitById(selectedUnitId);
        if (!selectedUnitId || !unit) return;
        navigator.clipboard.readText().then(text => {
            try {
                const parsed = JSON.parse(text);
                if (parsed && parsed.$realm_editor_type === 'unit-component-block' && Array.isArray(parsed.data)) {
                    pushToUndoStack();
                    unit[componentKey] = parsed.data;
                    saveChanges();
                    
                    let tagType = '';
                    if (componentKey === 'BuildOptions') tagType = 'build-options';
                    else if (componentKey === 'Abilities') tagType = 'abilities';
                    else if (componentKey === 'Weapons') tagType = 'weapons';
                    else if (componentKey === 'StartingItems') tagType = 'items';
                    else if (componentKey === 'Upgrades') tagType = 'upgrades';
                    else if (componentKey === 'StatusEffects') tagType = 'statuseffects';
                    else if (componentKey === 'SoundEvents') tagType = 'soundevents';
                    
                    if (tagType) {
                        renderTags(tagType, parsed.data);
                    }
                }
            } catch (e) {
                console.error("Paste Component block failed:", e);
            }
        });
    }

    document.addEventListener('click', e => {
        if (e.target.classList.contains('copy-unit-comp-btn')) {
            const key = e.target.dataset.key;
            copyUnitComponent(key);
            const oldText = e.target.innerHTML;
            e.target.innerHTML = '✅';
            setTimeout(() => e.target.innerHTML = oldText, 1500);
        } else if (e.target.classList.contains('paste-unit-comp-btn')) {
            if (isLocked) return;
            const key = e.target.dataset.key;
            pasteUnitComponent(key);
            const oldText = e.target.innerHTML;
            e.target.innerHTML = '✅';
            setTimeout(() => e.target.innerHTML = oldText, 1500);
        }
    });

    // --- TYPE LOCKED INPUTS FILTERING ---
    function setupNumericLockOnDynamicInputs() {
        document.querySelectorAll('input[type="number"]').forEach(input => {
            if (input.dataset.numericLocked) return;
            input.dataset.numericLocked = 'true';

            input.addEventListener('keypress', e => {
                const isFloat = input.step === 'any' || input.getAttribute('step') === 'any' || (input.step && input.step.includes('.'));
                const isMinZero = parseFloat(input.min) >= 0;
                
                const allowedChars = /[0-9]/;
                const char = String.fromCharCode(e.which || e.keyCode);
                
                if (allowedChars.test(char)) {
                    return true;
                }
                
                if (char === '.' && isFloat && !input.value.includes('.')) {
                    return true;
                }
                
                if (char === '-' && !isMinZero && input.selectionStart === 0 && !input.value.includes('-')) {
                    return true;
                }
                
                e.preventDefault();
                return false;
            });

            // Strip non-numbers on paste
            input.addEventListener('paste', e => {
                const text = (e.clipboardData || window.clipboardData).getData('text');
                const isFloat = input.step === 'any' || input.getAttribute('step') === 'any' || (input.step && input.step.includes('.'));
                
                let cleaned = text.replace(/[^0-9.-]/g, '');
                if (!isFloat) {
                    cleaned = cleaned.split('.')[0];
                }
                
                if (isNaN(parseFloat(cleaned))) {
                    e.preventDefault();
                }
            });
        });
    }

    // --- SMART CLONING PATTERN MATCHING SUFFIX (Unit_042 -> Unit_043) ---
    function incrementId(id, existingIds) {
        const match = id.match(/^(.*?)(_?)(\d+)$/);
        if (match) {
            const base = match[1];
            const separator = match[2];
            const numStr = match[3];
            let num = parseInt(numStr, 10);
            const padLen = numStr.length;
            
            while (true) {
                num++;
                const newNumStr = String(num).padStart(padLen, '0');
                const newId = `${base}${separator}${newNumStr}`;
                if (!existingIds.has(newId)) {
                    return newId;
                }
            }
        } else {
            let baseId = `${id}_copy`;
            let newId = baseId;
            let counter = 1;
            while (existingIds.has(newId)) {
                newId = `${baseId}_${counter}`;
                counter++;
            }
            return newId;
        }
    }

    function duplicateSelectedUnit() {
        if (!selectedUnitId) return;
        const sourceUnit = getUnitById(selectedUnitId);
        if (!sourceUnit) return;
        
        const existingIds = getExistingUnitIds();
        const domain = getActiveDomain();
        const prefix = domain === 'buildings' ? 'Building' : domain === 'resources' ? 'Resource' : domain === 'props' ? 'Prop' : 'Unit';
        const nextId = generateNextId(prefix, existingIds);
        
        const newUnit = JSON.parse(JSON.stringify(sourceUnit));
        newUnit.UnitId = nextId;
        newUnit.Name = `${sourceUnit.Name || 'New Entity'} (Copy)`;
        
        const targetArr = domain === 'buildings' ? (units.CustomBuildings = units.CustomBuildings || []) :
                          domain === 'resources' ? (units.CustomResources = units.CustomResources || []) :
                          domain === 'props' ? (units.CustomProps = units.CustomProps || []) :
                          (units.CustomUnits = units.CustomUnits || []);
        targetArr.push(newUnit);
        selectUnit(nextId);
        saveChanges();
    }

    function duplicateWeapon(index) {
        const list = units.CustomWeapons || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(w => w.WeaponId));
        const nextId = incrementId(source.WeaponId, existingIds);
        
        const newWeapon = JSON.parse(JSON.stringify(source));
        newWeapon.WeaponId = nextId;
        newWeapon.Name = `${source.Name || 'New Weapon'} (Copy)`;
        
        list.splice(index + 1, 0, newWeapon);
        units.CustomWeapons = list;
        saveChanges();
        renderCustomWeapons();
    }

    function duplicateAbility(index) {
        const list = units.CustomAbilities || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(a => a.AbilityId));
        const nextId = incrementId(source.AbilityId, existingIds);
        
        const newAbility = JSON.parse(JSON.stringify(source));
        newAbility.AbilityId = nextId;
        newAbility.Name = `${source.Name || 'New Ability'} (Copy)`;
        
        list.splice(index + 1, 0, newAbility);
        units.CustomAbilities = list;
        saveChanges();
        renderCustomAbilities();
    }

    function duplicateUpgrade(index) {
        const list = units.CustomUpgrades || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(u => u.UpgradeId));
        const nextId = incrementId(source.UpgradeId, existingIds);
        
        const newUpgrade = JSON.parse(JSON.stringify(source));
        newUpgrade.UpgradeId = nextId;
        newUpgrade.Name = `${source.Name || 'New Upgrade'} (Copy)`;
        
        list.splice(index + 1, 0, newUpgrade);
        units.CustomUpgrades = list;
        saveChanges();
        renderCustomUpgrades();
    }

    function duplicateItem(index) {
        const list = units.CustomItems || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(i => i.ItemId));
        const nextId = incrementId(source.ItemId, existingIds);
        
        const newItem = JSON.parse(JSON.stringify(source));
        newItem.ItemId = nextId;
        newItem.Name = `${source.Name || 'New Item'} (Copy)`;
        
        list.splice(index + 1, 0, newItem);
        units.CustomItems = list;
        saveChanges();
        renderCustomItems();
    }

    function showDeleteConfirm(displayName, id) {
        const overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.6);display:flex;align-items:center;justify-content:center;z-index:9999;';
        const dialog = document.createElement('div');
        dialog.style.cssText = 'background:var(--bg-secondary,#252526);border:1px solid var(--border-color,#3c3c3c);border-radius:8px;padding:24px;max-width:400px;box-shadow:0 8px 32px rgba(0,0,0,0.5);';
        dialog.innerHTML = '<p style="margin:0 0 16px 0;color:var(--text-primary,#d4d4d4);font-size:14px;">Are you sure you want to delete unit "' + displayName + '"?</p><div style="display:flex;gap:8px;justify-content:flex-end;"><button id="confirm-cancel" style="padding:8px 16px;border:1px solid var(--border-color,#3c3c3c);border-radius:6px;background:transparent;color:var(--text-primary,#d4d4d4);cursor:pointer;font-weight:600;">Cancel</button><button id="confirm-ok" style="padding:8px 16px;border:none;border-radius:6px;background:var(--danger-color,#f48771);color:#fff;cursor:pointer;font-weight:600;">Delete</button></div>';
        overlay.appendChild(dialog);
        document.body.appendChild(overlay);
        dialog.querySelector('#confirm-ok').addEventListener('click', function () { overlay.remove(); deleteUnit(id); });
        dialog.querySelector('#confirm-cancel').addEventListener('click', function () { overlay.remove(); });
        overlay.addEventListener('click', function (e) { if (e.target === overlay) overlay.remove(); });
    }

    function deleteUnit(id) {
        if (isLocked) return;
        try {
            pushToUndoStack();
            cascadeDelete('unit', id);
            units.CustomUnits = (units.CustomUnits || []).filter(u => u && u.UnitId !== id);
            units.CustomBuildings = (units.CustomBuildings || []).filter(u => u && u.UnitId !== id);
            units.CustomResources = (units.CustomResources || []).filter(u => u && u.UnitId !== id);
            units.CustomProps = (units.CustomProps || []).filter(u => u && u.UnitId !== id);
            if (selectedUnitId === id) {
                selectedUnitId = null;
                showEmptyState();
            }
            renderUnitList();
            saveChanges();
        } catch (err) {
            console.error('deleteUnit error:', err);
            vscode.postMessage({ type: 'ready' });
        }
    }

    addUnitBtn.addEventListener('click', () => {
        const domain = getActiveDomain();
        const defaultType = domain === 'units' ? 'units' :
                            domain === 'buildings' ? 'buildings' :
                            domain === 'resources' ? 'resources' :
                            domain === 'props' ? 'props' : 'units';
        const defaultArmor = domain === 'buildings' ? 'building' : 'light';
        const defaultPathing = domain === 'buildings' ? 32 : (domain === 'resources' || domain === 'props') ? 255 : 8;

        const prefix = domain === 'buildings' ? 'Building' : domain === 'resources' ? 'Resource' : domain === 'props' ? 'Prop' : 'Unit';
        const nextId = generateNextId(prefix, getExistingUnitIds());

        if (!Array.isArray(units.CustomUnits)) units.CustomUnits = [];
        if (!Array.isArray(units.CustomBuildings)) units.CustomBuildings = [];
        if (!Array.isArray(units.CustomResources)) units.CustomResources = [];
        if (!Array.isArray(units.CustomProps)) units.CustomProps = [];

        const targetArray = domain === 'buildings' ? units.CustomBuildings :
                            domain === 'resources' ? units.CustomResources :
                            domain === 'props' ? units.CustomProps : units.CustomUnits;

        if (domain === 'props') {
            targetArray.push({
                UnitId: nextId,
                Name: `New ${prefix}`,
                Description: `A decorative ${prefix.toLowerCase()} prop.`,
                PathingType: defaultPathing
            });
        } else if (domain === 'resources') {
            targetArray.push({
                UnitId: nextId,
                Name: `New ${prefix}`,
                Description: `Harvestable ${prefix.toLowerCase()} deposit.`,
                MaxCapacity: 2000.0,
                HarvestRate: 10.0,
                GrowthRate: 0.0,
                MaxWorkers: 5,
                PathingType: defaultPathing
            });
        } else {
            targetArray.push({
                UnitId: nextId,
                Name: `New ${prefix}`,
                Description: `A new ${prefix.toLowerCase()} entity.`,
                MaxHp: 100.0,
                Damage: 10.0,
                Range: 2.0,
                Armor: 0.0,
                Speed: 5.0,
                AttackCooldown: 1.5,
                ScanRadius: 10.0,
                CostGold: 100.0,
                CostWood: 0.0,
                CostStone: 0.0,
                ProductionTime: 10.0,
                PopCost: 1,
                AttackType: 'melee',
                ArmorType: defaultArmor,
                GoldBounty: 10.0,
                PathingType: defaultPathing
            });
        }

        selectUnit(nextId);
        saveChanges();
    });

    addCustomWeaponBtn.addEventListener('click', () => {
        if (!units.CustomWeapons) {
            units.CustomWeapons = [];
        }
        const list = units.CustomWeapons;
        const nextId = generateNextId('Weapon', new Set(list.map(w => w.WeaponId)));
        list.push({
            WeaponId: nextId,
            Name: 'New Weapon',
            Damage: 10,
            Range: 2.0,
            AttackCooldown: 1.5,
            AttackType: 'melee'
        });
        units.CustomWeapons = list;
        saveChanges();
        renderCustomWeapons();
    });

    addCustomAbilityBtn.addEventListener('click', () => {
        if (!units.CustomAbilities) {
            units.CustomAbilities = [];
        }
        const list = units.CustomAbilities;
        const nextId = generateNextId('Ability', new Set(list.map(a => a.AbilityId)));
        list.push({
            AbilityId: nextId,
            Name: 'New Ability',
            Description: 'A new spell effect.',
            AbilityType: 'target_spell'
        });
        units.CustomAbilities = list;
        saveChanges();
        renderCustomAbilities();
    });

    addCustomUpgradeBtn.addEventListener('click', () => {
        if (!units.CustomUpgrades) {
            units.CustomUpgrades = [];
        }
        const list = units.CustomUpgrades;
        const nextId = generateNextId('Upgrade', new Set(list.map(u => u.UpgradeId)));
        list.push({
            UpgradeId: nextId,
            Name: 'New Upgrade',
            Description: 'Increases unit stats.'
        });
        units.CustomUpgrades = list;
        saveChanges();
        renderCustomUpgrades();
    });

    addCustomItemBtn.addEventListener('click', () => {
        if (!units.CustomItems) {
            units.CustomItems = [];
        }
        const list = units.CustomItems;
        const nextId = generateNextId('Item', new Set(list.map(i => i.ItemId)));
        list.push({
            ItemId: nextId,
            Name: 'New Item',
            Description: 'A custom inventory item.',
            ItemClass: 'consumable'
        });
        units.CustomItems = list;
        saveChanges();
        renderCustomItems();
    });

    // --- SYSTEM FILE RESOLVE PATH & THUMBNAIL/AUDIO WARNINGS ---
    function resolvePath(path, callback) {
        const id = resolveCallbackId++;
        resolveCallbacks[id] = callback;
        vscode.postMessage({
            type: 'resolvePath',
            requestId: id,
            path: path
        });
    }

    function addWarningText(inputEl, msg) {
        let parent = inputEl.parentNode;
        if (parent.classList.contains('input-with-browse')) {
            parent = parent.parentNode;
        }
        let warning = parent.querySelector('.validation-warning-text');
        if (!warning) {
            warning = document.createElement('span');
            warning.className = 'validation-warning-text';
            parent.appendChild(warning);
        }
        warning.textContent = msg;
        inputEl.title = msg;
    }

    function removeWarningText(inputEl) {
        let parent = inputEl.parentNode;
        if (parent.classList.contains('input-with-browse')) {
            parent = parent.parentNode;
        }
        let warning = parent.querySelector('.validation-warning-text');
        if (warning) {
            warning.remove();
        }
        inputEl.removeAttribute('title');
    }

    function updateThumbnailForInput(inputEl) {
        if (!inputEl) return;
        const container = inputEl.closest('.input-with-browse');
        if (!container) return;
        
        let thumbContainer = container.nextElementSibling;
        if (!thumbContainer || !thumbContainer.classList.contains('thumbnail-preview-container')) {
            thumbContainer = document.createElement('div');
            thumbContainer.className = 'thumbnail-preview-container';
            container.parentNode.insertBefore(thumbContainer, container.nextSibling);
        }
        
        const val = inputEl.value.trim();
        if (!val) {
            thumbContainer.innerHTML = '';
            inputEl.classList.remove('input-warning');
            removeWarningText(inputEl);
            return;
        }
        
        // Supported extensions:
        const isImage = /\.(png|jpg|jpeg|gif|svg|bmp|webp|tga|dds)$/i.test(val);
        const is3DModel = /\.(glb|gltf|scn|tscn)$/i.test(val);
        const isAudio = /\.(ogg|wav|mp3)$/i.test(val);
        
        if (!isImage && !is3DModel && !isAudio) {
            thumbContainer.innerHTML = '';
            inputEl.classList.remove('input-warning');
            removeWarningText(inputEl);
            return;
        }
        
        thumbContainer.innerHTML = '<span class="thumbnail-loading">Resolving path...</span>';
        resolvePath(val, uri => {
            if (inputEl.value.trim() !== val) return;
            if (uri) {
                inputEl.classList.remove('input-warning');
                removeWarningText(inputEl);
                if (isImage) {
                    thumbContainer.innerHTML = `<img src="${uri}" class="thumbnail-preview-img" alt="Preview" />`;
                } else if (is3DModel) {
                    thumbContainer.innerHTML = `
                        <div class="model-preview-box" title="Click to open 3D model in VS Code">
                            <span>📦 View Model (${val.split('/').pop()})</span>
                        </div>
                    `;
                    const previewBox = thumbContainer.querySelector('.model-preview-box');
                    if (previewBox) {
                        previewBox.addEventListener('click', () => {
                            vscode.postMessage({
                                type: 'openFile',
                                path: val
                            });
                        });
                    }
                } else if (isAudio) {
                    thumbContainer.innerHTML = `
                        <div class="audio-preview-box">
                            <span>🔊 Audio Event: ${val.split('/').pop()}</span>
                        </div>
                    `;
                }
            } else {
                thumbContainer.innerHTML = `<span class="thumbnail-error">Asset file not found</span>`;
                inputEl.classList.add('input-warning');
                addWarningText(inputEl, `Asset file "${val}" not found in the game directories.`);
            }
        });
    }

    function updateAllThumbnails() {
        const imageInputs = [
            document.getElementById('prop-MinimapImage'),
            document.getElementById('prop-LoadingImage'),
            document.getElementById('prop-LoadingMusic'),
            document.getElementById('field-ModelPath'),
            document.getElementById('field-PortraitModelPath')
        ];
        
        document.querySelectorAll('.weapon-visual, .weapon-proj-model, .weapon-impact-visual, .weapon-sound, .weapon-impact-sound, .ability-visual, .ability-sound, .ability-icon, .item-icon').forEach(input => {
            imageInputs.push(input);
        });
        
        imageInputs.forEach(input => {
            if (input) updateThumbnailForInput(input);
        });
    }

    // --- OTHER CORE ACTIONS (Undo/Redo, Lock, Search, Hotkeys, Datalists, Warnings) ---
    function pushToUndoStack() {
        if (isUndoRedoAction) return;
        if (undoStack.length >= 50) {
            undoStack.shift();
        }
        undoStack.push(JSON.stringify(units));
        redoStack = [];
    }

    function undo() {
        if (isLocked) return;
        if (undoStack.length === 0) return;
        isUndoRedoAction = true;
        redoStack.push(JSON.stringify(units));
        const prevState = JSON.parse(undoStack.pop());
        units = prevState;
        saveChanges();
        
        const activeTabBtn = document.querySelector('.tab-btn.active');
        if (activeTabBtn) {
            switchTab(activeTabBtn.dataset.domain);
        }
        isUndoRedoAction = false;
    }

    function redo() {
        if (isLocked) return;
        if (redoStack.length === 0) return;
        isUndoRedoAction = true;
        undoStack.push(JSON.stringify(units));
        const nextState = JSON.parse(redoStack.pop());
        units = nextState;
        saveChanges();

        const activeTabBtn = document.querySelector('.tab-btn.active');
        if (activeTabBtn) {
            switchTab(activeTabBtn.dataset.domain);
        }
        isUndoRedoAction = false;
    }

    function applyLockState() {
        document.querySelectorAll('input, select, textarea, button').forEach(el => {
            if (el.id === 'toggle-lock-btn') return;
            if (el.classList.contains('browse-btn') || el.classList.contains('clear-btn') || el.classList.contains('btn-delete') || el.classList.contains('btn-duplicate-item') || el.classList.contains('remove-tag') || el.classList.contains('btn-delete-subitem') || el.classList.contains('add-subitem-btn') || el.classList.contains('copy-subtable-btn') || el.classList.contains('paste-subtable-btn') || el.classList.contains('copy-row-btn') || el.classList.contains('copy-unit-comp-btn') || el.classList.contains('paste-unit-comp-btn') || el.id === 'duplicate-unit-btn' || el.id === 'delete-unit-btn' || el.id === 'copy-unit-btn' || el.id === 'paste-unit-btn') {
                el.disabled = isLocked;
            } else {
                el.readOnly = isLocked;
                if (el.tagName === 'SELECT') {
                    el.disabled = isLocked;
                }
            }
        });
        const appContainer = document.querySelector('.app-container');
        if (appContainer) {
            appContainer.classList.toggle('editor-locked', isLocked);
        }
    }

    function showEmptyState() {
        hideAllForms();
        emptyState.classList.remove('hidden');
    }

    // save status UI
    function showSaving() {
        const status = document.getElementById('save-status');
        if (status) {
            status.className = 'save-status saving';
            status.textContent = '● Saving...';
        }
    }

    function showSaved() {
        const status = document.getElementById('save-status');
        if (status) {
            status.className = 'save-status saved';
            status.textContent = '● Saved';
        }
    }

    function updateDebugJson() {
        const debugPre = document.getElementById('debug-json-pre');
        const debugContainer = document.getElementById('debug-json-container');
        if (!debugPre || !debugContainer) return;
        
        if (debugMode) {
            debugContainer.classList.remove('hidden');
            debugPre.textContent = serializeDeterministic(units);
        } else {
            debugContainer.classList.add('hidden');
        }
    }

    function updateCatalogCardErrors() {
        const validation = getValidationErrors();
        
        const hasAbilityErrors = Object.keys(validation.abilities).length > 0;
        const hasUpgradeErrors = Object.keys(validation.upgrades).length > 0;
        const hasItemErrors = Object.keys(validation.items).length > 0;
        
        updateCardErrorBadge('abilities', hasAbilityErrors);
        updateCardErrorBadge('upgrades', hasUpgradeErrors);
        updateCardErrorBadge('items', hasItemErrors);
    }
    
    function updateCardErrorBadge(domain, hasErrors) {
        const btn = document.querySelector(`.tab-btn[data-domain="${domain}"]`);
        if (!btn) return;
        
        let badge = btn.querySelector('.card-error-badge');
        if (hasErrors) {
            if (!badge) {
                badge = document.createElement('span');
                badge.className = 'card-error-badge';
                badge.textContent = ' ⚠️';
                badge.style.color = 'var(--danger-color)';
                btn.appendChild(badge);
            }
        } else {
            if (badge) {
                badge.remove();
            }
        }
    }

    function getValidationErrors() {
        const errors = {
            units: {},
            weapons: {},
            abilities: {},
            upgrades: {},
            items: {}
        };

        const customUnitsList = getCustomUnits();
        const existingUnitIds = new Set(customUnitsList.map(u => u.UnitId).filter(Boolean));
        
        const existingWeaponIds = new Set((units.CustomWeapons || []).map(w => w.WeaponId).filter(Boolean));
        const existingAbilityIds = new Set((units.CustomAbilities || []).map(a => a.AbilityId).filter(Boolean));
        const existingUpgradeIds = new Set((units.CustomUpgrades || []).map(u => u.UpgradeId).filter(Boolean));
        const existingItemIds = new Set((units.CustomItems || []).map(i => i.ItemId).filter(Boolean));

        for (const unit of customUnitsList) {
            if (!unit || !unit.UnitId) continue;
            const id = unit.UnitId;
            
            const unitErrors = {};
            if (unit.BuildOptions) {
                unit.BuildOptions.forEach((targetId, index) => {
                    if (targetId && !existingUnitIds.has(targetId)) {
                        unitErrors[`BuildOptions_${index}`] = `Unit ID "${targetId}" does not exist.`;
                    }
                });
            }
            if (unit.Abilities) {
                unit.Abilities.forEach((targetId, index) => {
                    if (targetId && !existingAbilityIds.has(targetId)) {
                        unitErrors[`Abilities_${index}`] = `Ability ID "${targetId}" does not exist in Custom Abilities.`;
                    }
                });
            }
            if (unit.Weapons) {
                unit.Weapons.forEach((targetId, index) => {
                    if (targetId && !existingWeaponIds.has(targetId)) {
                        unitErrors[`Weapons_${index}`] = `Weapon ID "${targetId}" does not exist in Custom Weapons.`;
                    }
                });
            }
            if (unit.StartingItems) {
                unit.StartingItems.forEach((targetId, index) => {
                    if (targetId && !existingItemIds.has(targetId)) {
                        unitErrors[`StartingItems_${index}`] = `Item ID "${targetId}" does not exist in Custom Items.`;
                    }
                });
            }
            if (unit.Upgrades) {
                unit.Upgrades.forEach((targetId, index) => {
                    if (targetId && !existingUpgradeIds.has(targetId)) {
                        unitErrors[`Upgrades_${index}`] = `Upgrade ID "${targetId}" does not exist in Custom Upgrades.`;
                    }
                });
            }

            if (Object.keys(unitErrors).length > 0) {
                errors.units[id] = unitErrors;
            }
        }

        (units.CustomAbilities || []).forEach((item, index) => {
            const abiErrors = {};
            if (item.SummonedUnitId && !existingUnitIds.has(item.SummonedUnitId)) {
                abiErrors['SummonedUnitId'] = `Unit ID "${item.SummonedUnitId}" does not exist.`;
            }
            if (Object.keys(abiErrors).length > 0) {
                errors.abilities[index] = abiErrors;
            }
        });

        (units.CustomUpgrades || []).forEach((item, index) => {
            const upgErrors = {};
            if (item.AffectedUnitIds) {
                item.AffectedUnitIds.forEach((targetId, affectedIdx) => {
                    if (targetId && !existingUnitIds.has(targetId)) {
                        upgErrors[`AffectedUnitIds_${affectedIdx}`] = `Unit ID "${targetId}" does not exist.`;
                    }
                });
            }
            if (Object.keys(upgErrors).length > 0) {
                errors.upgrades[index] = upgErrors;
            }
        });

        (units.CustomItems || []).forEach((item, index) => {
            const itemErrors = {};
            if (item.UseAbility && !existingAbilityIds.has(item.UseAbility)) {
                itemErrors['UseAbility'] = `Ability ID "${item.UseAbility}" does not exist in Custom Abilities.`;
            }
            if (item.GrantedWeapons) {
                item.GrantedWeapons.forEach((targetId, weaponIdx) => {
                    if (targetId && !existingWeaponIds.has(targetId)) {
                        itemErrors[`GrantedWeapons_${weaponIdx}`] = `Weapon ID "${targetId}" does not exist in Custom Weapons.`;
                    }
                });
            }
            if (Object.keys(itemErrors).length > 0) {
                errors.items[index] = itemErrors;
            }
        });

        return errors;
    }

    function getTooltipDetails(type, id) {
        if (type === 'build-options' || type === 'suggest-units') {
            const u = getUnitById(id);
            if (u) {
                return { title: u.Name || id, desc: u.Description || 'No description.' };
            }
        } else if (type === 'weapons' || type === 'suggest-weapons') {
            const w = (units.CustomWeapons || []).find(x => x.WeaponId === id);
            if (w) {
                return { title: w.Name || id, desc: `Damage: ${w.Damage || 0}, Range: ${w.Range || 0}` };
            }
        } else if (type === 'abilities' || type === 'suggest-abilities') {
            const a = (units.CustomAbilities || []).find(x => x.AbilityId === id);
            if (a) {
                return { title: a.Name || id, desc: a.Description || 'No description.' };
            }
        } else if (type === 'items' || type === 'suggest-items') {
            const i = (units.CustomItems || []).find(x => x.ItemId === id);
            if (i) {
                return { title: i.Name || id, desc: i.Description || 'No description.' };
            }
        } else if (type === 'upgrades' || type === 'suggest-upgrades') {
            const u = (units.CustomUpgrades || []).find(x => x.UpgradeId === id);
            if (u) {
                return { title: u.Name || id, desc: u.Description || 'No description.' };
            }
        }
        return null;
    }

    function updateDatalists() {
        const customUnitsList = getCustomUnits();
        const weapons = units.CustomWeapons || [];
        const abilities = units.CustomAbilities || [];
        const upgrades = units.CustomUpgrades || [];
        const items = units.CustomItems || [];

        populateDatalist('suggest-units', customUnitsList.map(u => ({ id: u.UnitId, name: u.Name })));
        populateDatalist('suggest-weapons', weapons.map(w => ({ id: w.WeaponId, name: w.Name })));
        populateDatalist('suggest-abilities', abilities.map(a => ({ id: a.AbilityId, name: a.Name })));
        populateDatalist('suggest-upgrades', upgrades.map(u => ({ id: u.UpgradeId, name: u.Name })));
        populateDatalist('suggest-items', items.map(i => ({ id: i.ItemId, name: i.Name })));
    }

    function populateDatalist(id, items) {
        let dl = document.getElementById(id);
        if (!dl) {
            dl = document.createElement('datalist');
            dl.id = id;
            document.body.appendChild(dl);
        }
        dl.innerHTML = '';
        items.forEach(item => {
            if (!item.id) return;
            const opt = document.createElement('option');
            opt.value = item.id;
            opt.textContent = item.name ? `${item.name} (${item.id})` : item.id;
            dl.appendChild(opt);
        });
    }

    // --- EVENT WRAPPERS FOR FORM FIELDS (UNIT AND MAP PROPS) ---
    for (const [key, element] of Object.entries(formFields)) {
        if (!element) continue;

        element.addEventListener('change', e => {
            if (isLocked) return;
            if (!selectedUnitId) return;
            const targetUnit = getUnitById(selectedUnitId);
            if (!targetUnit) return;

            const val = e.target.type === 'checkbox' ? e.target.checked : e.target.value;

            if (key === 'UnitId') {
                const newId = val.trim();
                if (!newId || (newId !== selectedUnitId && getUnitById(newId))) {
                    element.value = selectedUnitId;
                    const inputEvent = new Event('input', { bubbles: true });
                    element.dispatchEvent(inputEvent);
                    return;
                }
                if (newId === selectedUnitId) return;

                let warningSpan = element.nextElementSibling;
                if (warningSpan && warningSpan.classList.contains('validation-warning-text')) {
                    warningSpan.remove();
                }
                element.classList.remove('input-warning');

                pushToUndoStack();
                cascadeRename('unit', selectedUnitId, newId);
                targetUnit.UnitId = newId;
                selectedUnitId = newId;
                editorSubtitle.textContent = `ID: ${newId}`;
                const breadcrumb = document.getElementById('editor-breadcrumb');
                if (breadcrumb) {
                    breadcrumb.textContent = `Units > ${newId}`;
                }
                renderUnitList();
                saveChanges();
                return;
            }

            let parsedVal = val;
            if (element.type === 'number') {
                parsedVal = element.step === '1' ? parseInt(val, 10) : parseFloat(val);
                if (isNaN(parsedVal)) {
                    parsedVal = 0;
                }
                if (parsedVal < 0) {
                    parsedVal = 0;
                    element.value = 0;
                }
            }

            pushToUndoStack();
            targetUnit[key] = parsedVal;
            if (key === 'Name') {
                editorTitle.textContent = parsedVal || 'Edit Unit';
                renderUnitList();
            } else if (key === 'Description' || key === 'CostGold' || key === 'CostWood' || key === 'CostStone' || key === 'AttackType') {
                renderUnitList();
            }

            saveChanges();
        });
    }

    for (const [key, element] of Object.entries(mapPropFields)) {
        if (!element) continue;

        element.addEventListener('change', e => {
            if (!units.MapProperties) {
                units.MapProperties = {};
            }

            const val = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
            let parsedVal = val;
            if (element.type === 'number') {
                parsedVal = element.step === '1' ? parseInt(val, 10) : parseFloat(val);
                if (isNaN(parsedVal)) parsedVal = 0;
                if (parsedVal < 0) {
                    parsedVal = 0;
                    element.value = 0;
                }
            }

            pushToUndoStack();
            units.MapProperties[key] = parsedVal;
            saveChanges();
        });
    }

    // --- FORM INPUT BUTTON BINDINGS ---
    addBuildOptionBtn.addEventListener('click', () => addTagItem('build-options'));
    buildOptionInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('build-options');
    });

    addAbilityBtn.addEventListener('click', () => addTagItem('abilities'));
    abilityInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('abilities');
    });

    addWeaponBtn.addEventListener('click', () => addTagItem('weapons'));
    weaponInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('weapons');
    });

    addItemBtn.addEventListener('click', () => addTagItem('items'));
    itemInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('items');
    });

    addUpgradeBtn.addEventListener('click', () => addTagItem('upgrades'));
    upgradeInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('upgrades');
    });

    addStatuseffectBtn.addEventListener('click', () => addTagItem('statuseffects'));
    statuseffectInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('statuseffects');
    });

    addSoundeventBtn.addEventListener('click', () => addTagItem('soundevents'));
    soundeventInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('soundevents');
    });

    addInstructionBtn.addEventListener('click', () => addTagItem('instructions'));
    instructionInput.addEventListener('keydown', e => {
        if (e.key === 'Enter') addTagItem('instructions');
    });

    addPlayerSlotBtn.addEventListener('click', () => {
        if (!units.MapProperties.PlayerSlots) {
            units.MapProperties.PlayerSlots = [];
        }
        pushToUndoStack();
        const list = units.MapProperties.PlayerSlots;
        let maxSlot = -1;
        list.forEach(slot => {
            if (slot.SlotId !== undefined && slot.SlotId > maxSlot) {
                maxSlot = slot.SlotId;
            }
        });
        const nextSlotId = maxSlot + 1;
        list.push({
            SlotId: nextSlotId,
            Name: `Player ${nextSlotId + 1}`,
            Color: nextSlotId === 0 ? 'red' : (nextSlotId === 1 ? 'blue' : (nextSlotId === 2 ? 'teal' : 'purple')),
            Faction: 'human',
            Controller: 'HumanPlayer'
        });
        units.MapProperties.PlayerSlots = list;
        saveChanges();
        renderPlayerSlots();
        renderTeams();
    });

    addTeamBtn.addEventListener('click', () => {
        if (!units.MapProperties.Teams) {
            units.MapProperties.Teams = [];
        }
        pushToUndoStack();
        const list = units.MapProperties.Teams;
        list.push({
            TeamName: `Team ${list.length + 1}`,
            Slots: []
        });
        units.MapProperties.Teams = list;
        saveChanges();
        renderTeams();
    });

    addChangelogBtn.addEventListener('click', () => {
        if (!units.MapProperties.Changelog) {
            units.MapProperties.Changelog = [];
        }
        pushToUndoStack();
        const list = units.MapProperties.Changelog;
        const now = new Date();
        const yyyy = now.getFullYear();
        const mm = String(now.getMonth() + 1).padStart(2, '0');
        const dd = String(now.getDate()).padStart(2, '0');
        list.push({
            Version: units.MapProperties.Version || '1.0.0',
            Date: `${yyyy}-${mm}-${dd}`,
            Details: 'New map release.'
        });
        units.MapProperties.Changelog = list;
        saveChanges();
        renderChangelog();
    });

    // File Picker click delegation
    document.addEventListener('click', async e => {
        const btn = e.target.closest('.browse-btn');
        if (btn) {
            if (isLocked) return;
            const inputId = btn.dataset.inputId;
            const fieldClass = btn.dataset.class;
            const fieldIndex = btn.dataset.index !== undefined ? parseInt(btn.dataset.index, 10) : null;
            const fileTypesStr = btn.dataset.fileTypes;
            const fileTypes = fileTypesStr ? fileTypesStr.split(',') : undefined;

            vscode.postMessage({
                type: 'browseFile',
                fieldId: inputId,
                fieldClass: fieldClass,
                fieldIndex: fieldIndex,
                fileTypes: fileTypes
            });
        }
    });

    // File Clear click delegation
    document.addEventListener('click', e => {
        const btn = e.target.closest('.clear-btn');
        if (btn) {
            if (isLocked) return;
            const inputId = btn.dataset.inputId;
            let inputEl = null;
            if (inputId) {
                inputEl = document.getElementById(inputId);
            } else {
                const group = btn.closest('.input-with-browse');
                if (group) inputEl = group.querySelector('input');
            }
            if (inputEl) {
                inputEl.value = '';
                const event = new Event('change', { bubbles: true });
                inputEl.dispatchEvent(event);
                updateThumbnailForInput(inputEl);
            }
        }
    });

    // Hotkey listener
    window.addEventListener('keydown', e => {
        const isMac = navigator.platform.toUpperCase().indexOf('MAC') >= 0;
        const isCmdOrCtrl = isMac ? e.metaKey : e.ctrlKey;
        
        if (e.altKey && e.key.toLowerCase() === 'n') {
            e.preventDefault();
            if (isLocked) return;
            const activeTabBtn = document.querySelector('.tab-btn.active');
            const domain = activeTabBtn ? activeTabBtn.dataset.domain : 'units';
            if (domain === 'weapons') addCustomWeaponBtn.click();
            else if (domain === 'abilities') addCustomAbilityBtn.click();
            else if (domain === 'upgrades') addCustomUpgradeBtn.click();
            else if (domain === 'items') addCustomItemBtn.click();
            else addUnitBtn.click();
        } else if (e.altKey && e.key.toLowerCase() === 'p') {
            e.preventDefault();
            switchTab('properties');
        } else if (e.altKey && e.key.toLowerCase() === 'w') {
            e.preventDefault();
            switchTab('weapons');
        } else if (e.altKey && e.key.toLowerCase() === 'a') {
            e.preventDefault();
            switchTab('abilities');
        } else if (e.altKey && e.key.toLowerCase() === 'u') {
            e.preventDefault();
            switchTab('upgrades');
        } else if (e.altKey && e.key.toLowerCase() === 'i') {
            e.preventDefault();
            switchTab('items');
        } else if (e.altKey && e.key.toLowerCase() === 'l') {
            e.preventDefault();
            if (toggleLockBtn) toggleLockBtn.click();
        } else if (e.altKey && e.key.toLowerCase() === 'e') {
            e.preventDefault();
            if (toggleButtonsBtn) toggleButtonsBtn.click();
        } else if (e.altKey && e.key.toLowerCase() === 'd') {
            const activeTabBtn = document.querySelector('.tab-btn.active');
            if (activeTabBtn && activeTabBtn.dataset.domain === 'units' && selectedUnitId) {
                e.preventDefault();
                if (isLocked) return;
                duplicateSelectedUnit();
            }
        } else if (isCmdOrCtrl && e.key.toLowerCase() === 'z') {
            e.preventDefault();
            undo();
        } else if (isCmdOrCtrl && (e.key.toLowerCase() === 'y' || (e.shiftKey && e.key.toLowerCase() === 'z'))) {
            e.preventDefault();
            redo();
        } else if (isCmdOrCtrl && e.key.toLowerCase() === 's') {
            e.preventDefault();
            saveChanges();
        } else if (isCmdOrCtrl && e.key.toLowerCase() === 'f') {
            e.preventDefault();
            searchInput.focus();
            searchInput.select();
        }
    });

    if (duplicateUnitBtn) {
        duplicateUnitBtn.addEventListener('click', () => {
            duplicateSelectedUnit();
        });
    }

    if (deleteUnitBtn) {
        deleteUnitBtn.addEventListener('click', () => {
            if (!selectedUnitId) return;
            const sourceUnit = getUnitById(selectedUnitId);
            showDeleteConfirm((sourceUnit && sourceUnit.Name) || selectedUnitId, selectedUnitId);
        });
    }

    if (toggleLockBtn) {
        toggleLockBtn.addEventListener('click', () => {
            isLocked = !isLocked;
            toggleLockBtn.classList.toggle('active', isLocked);
            toggleLockBtn.innerHTML = isLocked ? '🔒 Locked' : '🔓 Lock';
            applyLockState();
        });
    }

    if (toggleButtonsBtn) {
        toggleButtonsBtn.addEventListener('click', () => {
            const oldAddDelete = !document.querySelector('.app-container').classList.contains('hide-add-delete');
            toggleButtonsBtn.classList.toggle('active', oldAddDelete);
            toggleButtonsBtn.innerHTML = oldAddDelete ? '➕ Edit Ops' : '➖ Edit Ops';
            document.querySelector('.app-container').classList.toggle('hide-add-delete', oldAddDelete);
        });
    }

    if (toggleDebugBtn) {
        toggleDebugBtn.addEventListener('click', () => {
            debugMode = !debugMode;
            toggleDebugBtn.classList.toggle('active', debugMode);
            updateDebugJson();
        });
    }

    if (copyJsonBtn) {
        copyJsonBtn.addEventListener('click', () => {
            const text = serializeDeterministic(units);
            navigator.clipboard.writeText(text).then(() => {
                const oldText = copyJsonBtn.textContent;
                copyJsonBtn.textContent = 'Copied!';
                setTimeout(() => copyJsonBtn.textContent = oldText, 1500);
            });
        });
    }

    if (expandJsonBtn) {
        expandJsonBtn.addEventListener('click', () => {
            debugJsonExpanded = !debugJsonExpanded;
            const container = document.getElementById('debug-json-container');
            if (container) {
                container.classList.toggle('collapsed', !debugJsonExpanded);
                expandJsonBtn.textContent = debugJsonExpanded ? 'Collapse' : 'Expand';
            }
        });
    }

    searchInput.addEventListener('input', e => {
        searchQuery = e.target.value;
        renderUnitList();
    });

    // --- DETERMINISTIC SERIALIZATION (GIT-OPTIMIZED) ---
    function serializeDeterministic(data) {
        if (!data) return '';
        
        const lines = [];
        lines.push('{');
        
        // 1. MapProperties
        if (data.MapProperties) {
            lines.push('  "MapProperties": {');
            const props = data.MapProperties;
            const propKeys = Object.keys(props).filter(k => !['CustomWeapons', 'CustomAbilities', 'CustomUpgrades', 'CustomItems'].includes(k)).sort();
            
            propKeys.forEach((pKey, pIdx) => {
                const pVal = props[pKey];
                const comma = pIdx === propKeys.length - 1 ? '' : ',';
                if (pKey === 'PlayerSlots' && Array.isArray(pVal)) {
                    lines.push('    "PlayerSlots": [');
                    const sortedSlots = [...pVal].sort((a, b) => (a.SlotId - b.SlotId));
                    sortedSlots.forEach((slot, sIdx) => {
                        const slotLine = `      ${JSON.stringify(sortObjectKeys(slot))}${sIdx === sortedSlots.length - 1 ? '' : ','}`;
                        lines.push(slotLine);
                    });
                    lines.push(`    ]${comma}`);
                } else if (pKey === 'Teams' && Array.isArray(pVal)) {
                    lines.push('    "Teams": [');
                    pVal.forEach((team, tIdx) => {
                        const teamLine = `      ${JSON.stringify(sortObjectKeys(team))}${tIdx === pVal.length - 1 ? '' : ','}`;
                        lines.push(teamLine);
                    });
                    lines.push(`    ]${comma}`);
                } else if (pKey === 'Changelog' && Array.isArray(pVal)) {
                    lines.push('    "Changelog": [');
                    pVal.forEach((log, lIdx) => {
                        const logLine = `      ${JSON.stringify(sortObjectKeys(log))}${lIdx === pVal.length - 1 ? '' : ','}`;
                        lines.push(logLine);
                    });
                    lines.push(`    ]${comma}`);
                } else {
                    lines.push(`    "${pKey}": ${JSON.stringify(pVal)}${comma}`);
                }
            });
            lines.push('  },');
        }
        
        // CustomUnits, CustomBuildings, CustomResources, CustomProps
        const entityArrays = ['CustomUnits', 'CustomBuildings', 'CustomResources', 'CustomProps'];
        entityArrays.forEach(arrKey => {
            const list = (data[arrKey] && Array.isArray(data[arrKey])) ? data[arrKey] : [];
            const sorted = [...list].sort((a, b) => (a.UnitId || '').localeCompare(b.UnitId || ''));
            lines.push(`  "${arrKey}": [`);
            sorted.forEach((u, uIdx) => {
                const sortedU = sortObjectKeys(u);
                const uLine = `    ${JSON.stringify(sortedU)}${uIdx === sorted.length - 1 ? '' : ','}`;
                lines.push(uLine);
            });
            lines.push('  ],');
        });

        // 3. CustomAbilities
        const abisList = (data.CustomAbilities && Array.isArray(data.CustomAbilities)) ? data.CustomAbilities : [];
        const sortedAbis = [...abisList].sort((a, b) => (a.AbilityId || '').localeCompare(b.AbilityId || ''));
        lines.push('  "CustomAbilities": [');
        sortedAbis.forEach((a, aIdx) => {
            const sortedA = sortObjectKeys(a);
            const aLine = `    ${JSON.stringify(sortedA)}${aIdx === sortedAbis.length - 1 ? '' : ','}`;
            lines.push(aLine);
        });
        lines.push('  ],');

        // 4. CustomUpgrades
        const upgsList = (data.CustomUpgrades && Array.isArray(data.CustomUpgrades)) ? data.CustomUpgrades : [];
        const sortedUpgs = [...upgsList].sort((a, b) => (a.UpgradeId || '').localeCompare(b.UpgradeId || ''));
        lines.push('  "CustomUpgrades": [');
        sortedUpgs.forEach((u, uIdx) => {
            const sortedU = sortObjectKeys(u);
            const uLine = `    ${JSON.stringify(sortedU)}${uIdx === sortedUpgs.length - 1 ? '' : ','}`;
            lines.push(uLine);
        });
        lines.push('  ],');

        // 5. CustomItems
        const itemsList = (data.CustomItems && Array.isArray(data.CustomItems)) ? data.CustomItems : [];
        const sortedItems = [...itemsList].sort((a, b) => (a.ItemId || '').localeCompare(b.ItemId || ''));
        lines.push('  "CustomItems": [');
        sortedItems.forEach((item, iIdx) => {
            const sortedI = sortObjectKeys(item);
            const iLine = `    ${JSON.stringify(sortedI)}${iIdx === sortedItems.length - 1 ? '' : ','}`;
            lines.push(iLine);
        });
        lines.push('  ],');

        // 6. CustomWeapons
        const weaponsList = (data.CustomWeapons && Array.isArray(data.CustomWeapons)) ? data.CustomWeapons : [];
        const sortedWeapons = [...weaponsList].sort((a, b) => (a.WeaponId || '').localeCompare(b.WeaponId || ''));
        lines.push('  "CustomWeapons": [');
        sortedWeapons.forEach((w, wIdx) => {
            const sortedW = sortObjectKeys(w);
            const wLine = `    ${JSON.stringify(sortedW)}${wIdx === sortedWeapons.length - 1 ? '' : ','}`;
            lines.push(wLine);
        });
        lines.push('  ],');

        // 7. Assets
        if (data.Assets) {
            lines.push(`  "Assets": ${JSON.stringify(sortObjectKeys(data.Assets))}`);
        } else {
            lines.push('  "Assets": {}');
        }

        lines.push('}');
        return lines.join('\n');
    }

    function sortObjectKeys(obj, keyName = '') {
        if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) {
            return obj;
        }
        const sorted = {};
        const keys = Object.keys(obj);
        if (keyName !== 'textures') {
            keys.sort();
        }
        keys.forEach(key => {
            const val = obj[key];
            if (Array.isArray(val)) {
                sorted[key] = val.map(item => {
                    if (typeof item === 'object' && item !== null) {
                        return sortObjectKeys(item, key);
                    }
                    return item;
                });
            } else if (typeof val === 'object' && val !== null) {
                sorted[key] = sortObjectKeys(val, key);
            } else {
                sorted[key] = val;
            }
        });
        return sorted;
    }

    // --- RECURSIVE CASCADING (RENAME / DELETE REFERENCES) ---
    function cascadeRename(type, oldId, newId) {
        if (type === 'unit') {
            for (const unit of getCustomUnits()) {
                if (unit.BuildOptions) {
                    unit.BuildOptions = unit.BuildOptions.map(b => b === oldId ? newId : b);
                }
            }
            if (units.CustomAbilities) {
                units.CustomAbilities.forEach(a => {
                    if (a.SummonedUnitId === oldId) a.SummonedUnitId = newId;
                });
            }
            if (units.CustomUpgrades) {
                units.CustomUpgrades.forEach(upg => {
                    if (upg.AffectedUnitIds) {
                        upg.AffectedUnitIds = upg.AffectedUnitIds.map(u => u === oldId ? newId : u);
                    }
                });
            }
        } else if (type === 'weapon') {
            for (const unit of getCustomUnits()) {
                if (unit.Weapons) {
                    unit.Weapons = unit.Weapons.map(w => w === oldId ? newId : w);
                }
            }
            if (units.CustomItems) {
                units.CustomItems.forEach(item => {
                    if (item.GrantedWeapons) {
                        item.GrantedWeapons = item.GrantedWeapons.map(w => w === oldId ? newId : w);
                    }
                });
            }
        } else if (type === 'ability') {
            for (const unit of getCustomUnits()) {
                if (unit.Abilities) {
                    unit.Abilities = unit.Abilities.map(a => a === oldId ? newId : a);
                }
            }
            if (units.CustomItems) {
                units.CustomItems.forEach(item => {
                    if (item.UseAbility === oldId) item.UseAbility = newId;
                });
            }
        } else if (type === 'upgrade') {
            for (const unit of getCustomUnits()) {
                if (unit.Upgrades) {
                    unit.Upgrades = unit.Upgrades.map(u => u === oldId ? newId : u);
                }
            }
        } else if (type === 'item') {
            for (const unit of getCustomUnits()) {
                if (unit.StartingItems) {
                    unit.StartingItems = unit.StartingItems.map(i => i === oldId ? newId : i);
                }
            }
        }
    }

    function cascadeDelete(type, targetId) {
        if (type === 'unit') {
            for (const unit of getCustomUnits()) {
                if (unit.BuildOptions) {
                    unit.BuildOptions = unit.BuildOptions.filter(b => b !== targetId);
                }
            }
            if (units.CustomAbilities) {
                units.CustomAbilities.forEach(a => {
                    if (a.SummonedUnitId === targetId) delete a.SummonedUnitId;
                });
            }
            if (units.CustomUpgrades) {
                units.CustomUpgrades.forEach(upg => {
                    if (upg.AffectedUnitIds) {
                        upg.AffectedUnitIds = upg.AffectedUnitIds.filter(u => u !== targetId);
                    }
                });
            }
        } else if (type === 'weapon') {
            for (const unit of getCustomUnits()) {
                if (unit.Weapons) {
                    unit.Weapons = unit.Weapons.filter(w => w !== targetId);
                }
            }
            if (units.CustomItems) {
                units.CustomItems.forEach(item => {
                    if (item.GrantedWeapons) {
                        item.GrantedWeapons = item.GrantedWeapons.filter(w => w !== targetId);
                    }
                });
            }
        } else if (type === 'ability') {
            for (const unit of getCustomUnits()) {
                if (unit.Abilities) {
                    unit.Abilities = unit.Abilities.filter(a => a !== targetId);
                }
            }
            if (units.CustomItems) {
                units.CustomItems.forEach(item => {
                    if (item.UseAbility === targetId) delete item.UseAbility;
                });
            }
        } else if (type === 'upgrade') {
            for (const unit of getCustomUnits()) {
                if (unit.Upgrades) {
                    unit.Upgrades = unit.Upgrades.filter(u => u !== targetId);
                }
            }
        } else if (type === 'item') {
            for (const unit of getCustomUnits()) {
                if (unit.StartingItems) {
                    unit.StartingItems = unit.StartingItems.filter(i => i !== targetId);
                }
            }
        }
    }

    function selectCustomAssets() {
        selectedUnitId = '__custom_assets__';
        hideAllForms();
        const customAssetsForm = document.getElementById('custom-assets-form');
        if (customAssetsForm) {
            customAssetsForm.classList.remove('hidden');
        }
        renderAssetsMetadata();
    }

    function removeAsset(category, key, subCategory) {
        if (!units) return;

        let deleted = false;
        const containers = [
            units.Assets,
            units.MapProperties?.Assets,
            units
        ].filter(c => c && typeof c === 'object');

        for (const targetObj of containers) {
            if (subCategory && targetObj[category] && targetObj[category][subCategory] && key in targetObj[category][subCategory]) {
                delete targetObj[category][subCategory][key];
                if (Object.keys(targetObj[category][subCategory]).length === 0) {
                    delete targetObj[category][subCategory];
                }
                deleted = true;
                break;
            } else if (targetObj[category] && key in targetObj[category]) {
                delete targetObj[category][key];
                if (Object.keys(targetObj[category]).length === 0) {
                    delete targetObj[category];
                }
                deleted = true;
                break;
            }
        }

        if (deleted) {
            if (typeof vscode !== 'undefined' && vscode.postMessage) {
                vscode.postMessage({
                    type: 'deleteAsset',
                    category: category,
                    subCategory: subCategory,
                    key: key
                });
            }
            saveChanges();
            renderAssetsMetadata();
        }
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    let currentAssetTypeFilter = 'all';

    function formatCategoryName(cat) {
        if (cat === 'glb') return 'GLB';
        if (cat === 'vfx_spritesheets') return 'VFX Spritesheets';
        if (cat === 'sfx') return 'SFX';
        if (cat === 'music') return 'Music';
        if (cat === 'skyboxes') return 'Skyboxes';
        if (cat === 'decals') return 'Decals';
        if (cat === 'textures') return 'Textures';
        if (cat === 'icons') return 'Icons';
        return cat.charAt(0).toUpperCase() + cat.slice(1);
    }

    function renderAssetsMetadata() {
        const display = document.getElementById('assets-metadata-display');
        if (!display) return;

        let assets = {};
        const candidateKeys = ['textures', 'glb', 'decals', 'icons', 'vfx_spritesheets', 'sfx', 'music', 'skyboxes'];

        // Collect from units.Assets
        if (units?.Assets && typeof units.Assets === 'object') {
            Object.assign(assets, units.Assets);
        }
        // Collect from units.MapProperties.Assets
        if (units?.MapProperties?.Assets && typeof units.MapProperties.Assets === 'object') {
            Object.assign(assets, units.MapProperties.Assets);
        }
        // Collect candidate keys directly on units root or MapProperties root
        candidateKeys.forEach(k => {
            if (units && units[k] && typeof units[k] === 'object' && Object.keys(units[k]).length > 0) {
                assets[k] = Object.assign({}, assets[k] || {}, units[k]);
            }
            if (units?.MapProperties && units.MapProperties[k] && typeof units.MapProperties[k] === 'object' && Object.keys(units.MapProperties[k]).length > 0) {
                assets[k] = Object.assign({}, assets[k] || {}, units.MapProperties[k]);
            }
        });

        const filterSelect = document.getElementById('asset-type-filter-select');
        if (filterSelect) {
            currentAssetTypeFilter = filterSelect.value || 'all';
        }

        const availableCategories = Object.keys(assets).filter(k => assets[k] && typeof assets[k] === 'object' && Object.keys(assets[k]).length > 0);

        if (filterSelect) {
            let selectHtml = `<option value="all" ${currentAssetTypeFilter === 'all' ? 'selected' : ''}>All</option>`;
            availableCategories.forEach(cat => {
                selectHtml += `<option value="${cat}" ${currentAssetTypeFilter === cat ? 'selected' : ''}>${escapeHtml(formatCategoryName(cat))}</option>`;
            });
            filterSelect.innerHTML = selectHtml;

            if (!filterSelect.getAttribute('data-listener-attached')) {
                filterSelect.setAttribute('data-listener-attached', 'true');
                filterSelect.addEventListener('change', (e) => {
                    currentAssetTypeFilter = e.target.value;
                    renderAssetsMetadata();
                });
            }
        }

        if (!assets || availableCategories.length === 0) {
            display.innerHTML = '<em>No assets registered in metadata.json yet. Use the buttons above to import assets!</em>';
            return;
        }

        const categoriesToRender = availableCategories.filter(cat => currentAssetTypeFilter === 'all' || currentAssetTypeFilter === cat);

        if (categoriesToRender.length === 0) {
            display.innerHTML = `<em>No assets found matching filter "${escapeHtml(formatCategoryName(currentAssetTypeFilter))}".</em>`;
            return;
        }

        let html = '<div class="asset-list" style="display: flex; flex-direction: column; gap: 8px;">';
        
        categoriesToRender.forEach(category => {
            const catObj = assets[category];
            if (!catObj || typeof catObj !== 'object') return;

            html += `<div class="asset-category" style="margin-bottom: 8px;">`;
            html += `<div style="font-weight: bold; color: var(--vscode-symbolIcon-propertyForeground, #4ec9b0); margin-bottom: 4px; text-transform: uppercase; font-size: 11px;">${escapeHtml(category)}</div>`;

            const isSubCategorized = category === 'glb';
            if (isSubCategorized) {
                Object.keys(catObj).forEach(subCat => {
                    const subObj = catObj[subCat];
                    if (typeof subObj === 'object' && subObj !== null) {
                        html += `<div style="margin-left: 8px; font-weight: 600; color: var(--text-muted); font-size: 11px; margin-top: 4px;">${escapeHtml(subCat)}</div>`;
                        Object.keys(subObj).forEach(itemKey => {
                            const relAssetPath = `Assets/models/${subCat}/${itemKey}`;
                            html += `<div style="margin-left: 12px; margin-top: 2px;">`;
                            html += `<div class="asset-item-row" style="display: flex; justify-content: space-between; align-items: center; background: rgba(255,255,255,0.04); padding: 4px 8px; border-radius: 4px;">`;
                            html += `<span style="font-family: monospace;">${escapeHtml(itemKey)}</span>`;
                            html += `<div style="display: flex; gap: 6px; align-items: center;">`;
                            html += `<select class="select-migrate-target" style="background: rgba(0,0,0,0.4); color: #ccc; border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; font-size: 11px; padding: 1px 4px;">`;
                            ['units', 'buildings', 'resources', 'props'].forEach(sc => {
                                html += `<option value="glb:${sc}" ${sc === subCat ? 'selected' : ''}>Model: ${sc}</option>`;
                            });
                            html += `</select>`;
                            html += `<button type="button" class="btn small-btn btn-migrate-asset" data-category="${escapeHtml(category)}" data-subcategory="${escapeHtml(subCat)}" data-key="${escapeHtml(itemKey)}" title="Change Default Category" style="background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; cursor: pointer; font-size: 11px; padding: 2px 6px; color: #fff;">🔄 Move</button>`;
                            html += `<button type="button" class="btn small-btn btn-preview-asset" data-category="${escapeHtml(category)}" data-subcategory="${escapeHtml(subCat)}" data-key="${escapeHtml(itemKey)}" data-path="${escapeHtml(relAssetPath)}" title="Preview asset" style="background: transparent; border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; cursor: pointer; font-size: 11px; padding: 2px 6px; color: #fff;">👁️ Preview</button>`;
                            html += `<button type="button" class="btn small-btn btn-delete-asset" data-category="${escapeHtml(category)}" data-subcategory="${escapeHtml(subCat)}" data-key="${escapeHtml(itemKey)}" title="Remove asset" style="color: #f44336; background: transparent; border: none; cursor: pointer; font-weight: bold; font-size: 14px; padding: 0 4px;">✕</button>`;
                            html += `</div></div>`;
                            html += `<div class="asset-preview-container hidden" style="margin-top: 4px; padding: 8px; background: rgba(0,0,0,0.3); border-radius: 4px; text-align: center;"></div>`;
                            html += `</div>`;
                        });
                    }
                });
            } else {
                Object.keys(catObj).forEach(itemKey => {
                    let relAssetPath = `Assets/${category}/${itemKey}`;
                    let itemVal = catObj[itemKey];
                    let cols = 4, rows = 4;
                    if (typeof itemVal === 'object' && itemVal !== null) {
                        cols = itemVal.columns || 4;
                        rows = itemVal.rows || 4;
                    }

                    if (category === 'vfx_spritesheets') relAssetPath = `Assets/vfx/${itemKey}`;
                    else if (category === 'skyboxes') relAssetPath = `Assets/skyboxes/${itemKey}`;
                    else if (category === 'decals') relAssetPath = `Assets/decals/${itemKey}`;
                    else if (category === 'icons') relAssetPath = `Assets/icons/${itemKey}`;
                    else if (category === 'textures') relAssetPath = `Assets/textures/${itemKey}`;
                    else if (category === 'sfx') relAssetPath = `Assets/audio/sfx/${itemKey}`;
                    else if (category === 'music') relAssetPath = `Assets/audio/music/${itemKey}`;

                    const canMigrateRawPng = category === 'decals' || category === 'icons' || category === 'skyboxes';
                    const canMigrateAudio = category === 'sfx' || category === 'music';

                    html += `<div style="margin-top: 2px;">`;
                    html += `<div class="asset-item-row" style="display: flex; justify-content: space-between; align-items: center; background: rgba(255,255,255,0.04); padding: 4px 8px; border-radius: 4px;">`;
                    html += `<span style="font-family: monospace;">${escapeHtml(itemKey)}</span>`;
                    html += `<div style="display: flex; gap: 6px; align-items: center;">`;
                    if (canMigrateRawPng) {
                        html += `<select class="select-migrate-target" style="background: rgba(0,0,0,0.4); color: #ccc; border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; font-size: 11px; padding: 1px 4px;">`;
                        html += `<option value="decals" ${category === 'decals' ? 'selected' : ''}>Decal</option>`;
                        html += `<option value="icons" ${category === 'icons' ? 'selected' : ''}>Icon</option>`;
                        html += `<option value="skyboxes" ${category === 'skyboxes' ? 'selected' : ''}>Skybox</option>`;
                        html += `</select>`;
                        html += `<button type="button" class="btn small-btn btn-migrate-asset" data-category="${escapeHtml(category)}" data-key="${escapeHtml(itemKey)}" title="Migrate Asset Type" style="background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; cursor: pointer; font-size: 11px; padding: 2px 6px; color: #fff;">🔄 Move</button>`;
                    } else if (canMigrateAudio) {
                        html += `<select class="select-migrate-target" style="background: rgba(0,0,0,0.4); color: #ccc; border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; font-size: 11px; padding: 1px 4px;">`;
                        html += `<option value="sfx" ${category === 'sfx' ? 'selected' : ''}>Audio: SFX</option>`;
                        html += `<option value="music" ${category === 'music' ? 'selected' : ''}>Audio: Music</option>`;
                        html += `</select>`;
                        html += `<button type="button" class="btn small-btn btn-migrate-asset" data-category="${escapeHtml(category)}" data-key="${escapeHtml(itemKey)}" title="Migrate Asset Type" style="background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; cursor: pointer; font-size: 11px; padding: 2px 6px; color: #fff;">🔄 Move</button>`;
                    }
                    html += `<button type="button" class="btn small-btn btn-preview-asset" data-category="${escapeHtml(category)}" data-key="${escapeHtml(itemKey)}" data-cols="${cols}" data-rows="${rows}" data-path="${escapeHtml(relAssetPath)}" title="Preview asset" style="background: transparent; border: 1px solid rgba(255,255,255,0.2); border-radius: 3px; cursor: pointer; font-size: 11px; padding: 2px 6px; color: #fff;">👁️ Preview</button>`;
                    html += `<button type="button" class="btn small-btn btn-delete-asset" data-category="${escapeHtml(category)}" data-key="${escapeHtml(itemKey)}" title="Remove asset" style="color: #f44336; background: transparent; border: none; cursor: pointer; font-weight: bold; font-size: 14px; padding: 0 4px;">✕</button>`;
                    html += `</div></div>`;
                    if (category === 'textures') {
                        let tileMode = (typeof itemVal === 'object' && itemVal !== null && (itemVal.Tile_Mode || itemVal.tile_mode)) ? (itemVal.Tile_Mode || itemVal.tile_mode) : 'Stochastic';
                        let uvScale = (typeof itemVal === 'object' && itemVal !== null && (itemVal.UV_Scale !== undefined || itemVal.uv_scale !== undefined)) ? parseFloat(itemVal.UV_Scale ?? itemVal.uv_scale) : 1.0;
                        if (isNaN(uvScale)) uvScale = 1.0;
                        let stochTileSize = (typeof itemVal === 'object' && itemVal !== null && (itemVal.Stochastic_Tile_Size !== undefined || itemVal.stochastic_tile_size !== undefined)) ? parseFloat(itemVal.Stochastic_Tile_Size ?? itemVal.stochastic_tile_size) : 1.0;
                        if (isNaN(stochTileSize)) stochTileSize = 1.0;
                        let crossFade = (typeof itemVal === 'object' && itemVal !== null && (itemVal.Cross_Fade !== undefined || itemVal.cross_fade !== undefined || itemVal.Grid_Cross_Fade !== undefined || itemVal.grid_cross_fade !== undefined)) ? parseFloat(itemVal.Cross_Fade ?? itemVal.cross_fade ?? itemVal.Grid_Cross_Fade ?? itemVal.grid_cross_fade) : 5.0;
                        if (isNaN(crossFade)) crossFade = 5.0;

                        html += `<div class="texture-stochastic-controls" data-key="${escapeHtml(itemKey)}" style="margin-top: 6px; margin-bottom: 8px; padding: 8px 12px; background: var(--vscode-input-background, #1e1e24); border: 1px solid var(--vscode-input-border, rgba(255,255,255,0.15)); border-left: 4px solid var(--vscode-symbolIcon-propertyForeground, #4ec9b0); border-radius: 6px; display: flex; flex-direction: column; gap: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.3);">`;
                        
                        html += `<div style="display: flex; align-items: center; justify-content: space-between; gap: 12px;">`;
                        html += `<label title="Controls texture tiling method. 'Stochastic' uses non-repeating procedural triangular grid sampling; 'Grid' uses standard UV tiling." style="font-size: 12px; color: var(--vscode-foreground, #f0f0f0); font-weight: 600;">Tile_Mode:</label>`;
                        html += `<select class="input-texture-tile-mode" data-key="${escapeHtml(itemKey)}" title="Controls texture tiling method. 'Stochastic' uses non-repeating procedural triangular grid sampling; 'Grid' uses standard UV tiling." style="background: var(--vscode-editor-background, #141416); color: var(--vscode-input-foreground, #ffffff); border: 1px solid var(--vscode-input-border, rgba(255,255,255,0.25)); border-radius: 4px; font-size: 12px; font-weight: 600; padding: 3px 8px; cursor: pointer;">`;
                        html += `<option value="Stochastic" ${tileMode === 'Stochastic' ? 'selected' : ''}>Stochastic (default)</option>`;
                        html += `<option value="Grid" ${tileMode === 'Grid' ? 'selected' : ''}>Grid</option>`;
                        html += `</select>`;
                        html += `</div>`;

                        html += `<div style="display: flex; align-items: center; justify-content: space-between; gap: 12px;">`;
                        html += `<label title="Multiplier to scale texture UV coordinates (range 0.1 to 4.0)." style="font-size: 12px; color: var(--vscode-foreground, #f0f0f0); font-weight: 600;">UV_Scale: <span class="lbl-uv-scale-val" style="font-family: monospace; color: #4ec9b0; background: rgba(78, 201, 176, 0.15); border: 1px solid rgba(78, 201, 176, 0.3); border-radius: 3px; padding: 1px 6px; font-weight: 700; font-size: 12px;">${uvScale.toFixed(2)}</span></label>`;
                        html += `<input type="range" class="input-texture-uv-scale" data-key="${escapeHtml(itemKey)}" min="0.1" max="4.0" step="0.05" value="${uvScale}" title="Multiplier to scale texture UV coordinates (range 0.1 to 4.0)." style="width: 130px; cursor: pointer;" />`;
                        html += `</div>`;

                        html += `<div class="row-stochastic-tile-size" style="display: ${tileMode === 'Grid' ? 'none' : 'flex'}; align-items: center; justify-content: space-between; gap: 12px;">`;
                        html += `<label title="Scale multiplier for procedural stochastic sampling grid cells (range 0.5 to 3.0)." style="font-size: 12px; color: var(--vscode-foreground, #f0f0f0); font-weight: 600;">Stochastic Tile Size: <span class="lbl-stoch-size-val" style="font-family: monospace; color: #4ec9b0; background: rgba(78, 201, 176, 0.15); border: 1px solid rgba(78, 201, 176, 0.3); border-radius: 3px; padding: 1px 6px; font-weight: 700; font-size: 12px;">${stochTileSize.toFixed(2)}</span></label>`;
                        html += `<input type="range" class="input-texture-stoch-size" data-key="${escapeHtml(itemKey)}" min="0.5" max="3.0" step="0.05" value="${stochTileSize}" title="Scale multiplier for procedural stochastic sampling grid cells (range 0.5 to 3.0)." style="width: 130px; cursor: pointer;" />`;
                        html += `</div>`;

                        html += `<div class="row-grid-cross-fade" style="display: ${tileMode === 'Grid' ? 'flex' : 'none'}; align-items: center; justify-content: space-between; gap: 12px;">`;
                        html += `<label title="Border cross-fade width along tile seams (range 0% to 10% of UV space, default 5%)." style="font-size: 12px; color: var(--vscode-foreground, #f0f0f0); font-weight: 600;">Cross-Fade: <span class="lbl-cross-fade-val" style="font-family: monospace; color: #4ec9b0; background: rgba(78, 201, 176, 0.15); border: 1px solid rgba(78, 201, 176, 0.3); border-radius: 3px; padding: 1px 6px; font-weight: 700; font-size: 12px;">${crossFade.toFixed(1)}%</span></label>`;
                        html += `<input type="range" class="input-texture-cross-fade" data-key="${escapeHtml(itemKey)}" min="0" max="10" step="0.5" value="${crossFade}" title="Border cross-fade width along tile seams (range 0% to 10% of UV space, default 5%)." style="width: 130px; cursor: pointer;" />`;
                        html += `</div>`;

                        html += `</div>`;
                    }

                    html += `<div class="asset-preview-container hidden" style="margin-top: 4px; padding: 8px; background: rgba(0,0,0,0.3); border-radius: 4px; text-align: center;"></div>`;
                    html += `</div>`;
                });
            }

            html += `</div>`;
        });

        html += '</div>';
        display.innerHTML = html;

        function updateTextureProperty(itemKey, updateFn) {
            let container = null;
            if (units?.Assets?.textures) container = units.Assets.textures;
            else if (units?.MapProperties?.Assets?.textures) container = units.MapProperties.Assets.textures;
            else if (units?.textures) container = units.textures;
            if (!container) {
                if (!units.Assets) units.Assets = {};
                if (!units.Assets.textures) units.Assets.textures = {};
                container = units.Assets.textures;
            }
            let itemVal = container[itemKey];
            if (typeof itemVal !== 'object' || itemVal === null) {
                itemVal = { hash: String(itemVal || '') };
            }
            updateFn(itemVal);
            container[itemKey] = itemVal;
            saveChanges();
            
            const tileMode = (itemVal.Tile_Mode || itemVal.tile_mode) ?? 'Stochastic';
            const uvScale = parseFloat(itemVal.UV_Scale ?? itemVal.uv_scale ?? 1.0);
            const stochTileSize = parseFloat(itemVal.Stochastic_Tile_Size ?? itemVal.stochastic_tile_size ?? 1.0);
            const crossFade = parseFloat(itemVal.Cross_Fade ?? itemVal.cross_fade ?? itemVal.Grid_Cross_Fade ?? itemVal.grid_cross_fade ?? 5.0);

            const ipcPort = new URLSearchParams(window.location.search).get('ipcPort') || '8092';
            fetch(`http://127.0.0.1:${ipcPort}/api/`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    action: 'updateTextureParam',
                    swatchName: itemKey,
                    tileMode: tileMode,
                    uvScale: uvScale,
                    stochasticTileSize: stochTileSize,
                    crossFade: crossFade
                })
            }).catch(() => {});
        }

        display.querySelectorAll('.input-texture-tile-mode').forEach(sel => {
            sel.addEventListener('change', (e) => {
                const key = e.target.getAttribute('data-key');
                const val = e.target.value;
                const parentRow = e.target.closest('.texture-stochastic-controls');
                if (parentRow) {
                    const stochRow = parentRow.querySelector('.row-stochastic-tile-size');
                    if (stochRow) stochRow.style.display = (val === 'Grid') ? 'none' : 'flex';
                    const gridRow = parentRow.querySelector('.row-grid-cross-fade');
                    if (gridRow) gridRow.style.display = (val === 'Grid') ? 'flex' : 'none';
                }
                updateTextureProperty(key, (itemVal) => {
                    itemVal.Tile_Mode = val;
                    itemVal.tile_mode = val;
                });
            });
        });

        display.querySelectorAll('.input-texture-variants').forEach(sel => {
            sel.addEventListener('change', (e) => {
                const key = e.target.getAttribute('data-key');
                const val = (e.target.value === 'true');
                updateTextureProperty(key, (itemVal) => {
                    itemVal.Variants = val;
                    itemVal.variants = val;
                });
                const ipcPort = new URLSearchParams(window.location.search).get('ipcPort') || '8092';
                fetch(`http://127.0.0.1:${ipcPort}/api/`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'reloadMetadata' })
                }).catch(() => {});
            });
        });

        display.querySelectorAll('.input-texture-uv-scale').forEach(input => {
            const handleUvChange = (e) => {
                const key = e.target.getAttribute('data-key');
                const val = parseFloat(e.target.value);
                const parentRow = e.target.closest('.texture-stochastic-controls');
                if (parentRow) {
                    const lbl = parentRow.querySelector('.lbl-uv-scale-val');
                    if (lbl) lbl.textContent = val.toFixed(2);
                }
                updateTextureProperty(key, (itemVal) => {
                    itemVal.UV_Scale = val;
                    itemVal.uv_scale = val;
                });
            };
            input.addEventListener('input', handleUvChange);
            input.addEventListener('change', handleUvChange);
        });

        display.querySelectorAll('.input-texture-stoch-size').forEach(input => {
            const handleStochChange = (e) => {
                const key = e.target.getAttribute('data-key');
                const val = parseFloat(e.target.value);
                const parentRow = e.target.closest('.texture-stochastic-controls');
                if (parentRow) {
                    const lbl = parentRow.querySelector('.lbl-stoch-size-val');
                    if (lbl) lbl.textContent = val.toFixed(2);
                }
                updateTextureProperty(key, (itemVal) => {
                    itemVal.Stochastic_Tile_Size = val;
                    itemVal.stochastic_tile_size = val;
                });
            };
            input.addEventListener('input', handleStochChange);
            input.addEventListener('change', handleStochChange);
        });

        display.querySelectorAll('.input-texture-cross-fade').forEach(input => {
            const handleCrossFadeChange = (e) => {
                const key = e.target.getAttribute('data-key');
                const val = parseFloat(e.target.value);
                const parentRow = e.target.closest('.texture-stochastic-controls');
                if (parentRow) {
                    const lbl = parentRow.querySelector('.lbl-cross-fade-val');
                    if (lbl) lbl.textContent = `${val.toFixed(1)}%`;
                }
                updateTextureProperty(key, (itemVal) => {
                    itemVal.Cross_Fade = val;
                    itemVal.cross_fade = val;
                });
            };
            input.addEventListener('input', handleCrossFadeChange);
            input.addEventListener('change', handleCrossFadeChange);
        });

        // Attach migration event handlers
        display.querySelectorAll('.btn-migrate-asset').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const target = e.currentTarget;
                const cat = target.getAttribute('data-category');
                const subCat = target.getAttribute('data-subcategory');
                const key = target.getAttribute('data-key');
                const row = target.closest('.asset-item-row');
                const sel = row ? row.querySelector('.select-migrate-target') : null;
                if (!sel) return;
                const val = sel.value; // e.g. "glb:character", "decals", "icons", "sfx"
                let toCat = val;
                let toSubCat = undefined;
                if (val.includes(':')) {
                    const parts = val.split(':');
                    toCat = parts[0];
                    toSubCat = parts[1];
                }
                if (cat === toCat && subCat === toSubCat) return;
                vscode.postMessage({
                    type: 'migrateAsset',
                    fromCategory: cat,
                    fromSubCategory: subCat,
                    key: key,
                    toCategory: toCat,
                    toSubCategory: toSubCat
                });
            });
        });

        // Attach delete event handlers
        display.querySelectorAll('.btn-delete-asset').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const target = e.currentTarget;
                const cat = target.getAttribute('data-category');
                const subCat = target.getAttribute('data-subcategory');
                const key = target.getAttribute('data-key');
                if (cat && key) {
                    removeAsset(cat, key, subCat);
                }
            });
        });

        // Attach preview event handlers
        display.querySelectorAll('.btn-preview-asset').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const target = e.currentTarget;
                const cat = target.getAttribute('data-category');
                const pathStr = target.getAttribute('data-path');
                const cols = parseInt(target.getAttribute('data-cols'), 10) || 4;
                const rows = parseInt(target.getAttribute('data-rows'), 10) || 4;
                const parentRow = target.closest('div[style*="margin-top"]');
                const previewContainer = parentRow ? parentRow.querySelector('.asset-preview-container') : null;
                if (!previewContainer) return;

                if (!previewContainer.classList.contains('hidden')) {
                    previewContainer.classList.add('hidden');
                    previewContainer.innerHTML = '';
                    if (previewContainer._animInterval) {
                        clearInterval(previewContainer._animInterval);
                        previewContainer._animInterval = null;
                    }
                    return;
                }

                previewContainer.classList.remove('hidden');
                previewContainer.innerHTML = '<span style="color: var(--text-muted); font-size: 11px;">Loading preview...</span>';

                const reqId = 'preview_' + Math.random().toString(36).substring(2, 9);

                const handleResult = (evt) => {
                    const msg = evt.data || (evt.detail ? evt.detail : null);
                    if (!msg) return;
                    if ((msg.action === 'snapshotResult' || msg.type === 'snapshotResult' || msg.type === 'requestGodotSnapshot') && (msg.requestId === reqId || msg.filePath === pathStr)) {
                        if (msg.base64) {
                            if (previewContainer._snapshotTimeout) clearTimeout(previewContainer._snapshotTimeout);
                            previewContainer.innerHTML = `<img src="data:image/png;base64,${msg.base64}" style="max-width: 256px; max-height: 256px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.2);" title="Godot Live Snapshot (256x256)" />`;
                            window.removeEventListener('message', handleResult);
                            if (window.chrome && window.chrome.webview) window.chrome.webview.removeEventListener('message', handleResult);
                            return;
                        }
                    }
                    if (msg.type === 'resolvePathResult' && msg.requestId === reqId) {
                        const fileUri = msg.uri;
                        if (!fileUri) {
                            previewContainer.innerHTML = '<span style="color: #f44336; font-size: 11px;">File not found</span>';
                            return;
                        }

                        if (cat === 'vfx_spritesheets') {
                            const img = new Image();
                            img.crossOrigin = 'anonymous';
                            img.onload = () => {
                                const canvas = document.createElement('canvas');
                                const frameWidth = Math.floor(img.width / cols);
                                const frameHeight = Math.floor(img.height / rows);
                                canvas.width = frameWidth;
                                canvas.height = frameHeight;
                                canvas.style.maxWidth = '200px';
                                canvas.style.maxHeight = '200px';
                                canvas.style.border = '1px solid rgba(255,255,255,0.2)';
                                canvas.style.borderRadius = '4px';

                                const ctx = canvas.getContext('2d');
                                let currentFrame = 0;
                                const totalFrames = cols * rows;

                                const renderFrame = () => {
                                    const col = currentFrame % cols;
                                    const row = Math.floor(currentFrame / cols);
                                    ctx.clearRect(0, 0, frameWidth, frameHeight);
                                    ctx.drawImage(img, col * frameWidth, row * frameHeight, frameWidth, frameHeight, 0, 0, frameWidth, frameHeight);
                                    currentFrame = (currentFrame + 1) % totalFrames;
                                };

                                renderFrame();
                                previewContainer.innerHTML = '';
                                previewContainer.appendChild(canvas);
                                if (previewContainer._animInterval) clearInterval(previewContainer._animInterval);
                                previewContainer._animInterval = setInterval(renderFrame, 80);
                            };
                            img.onerror = () => {
                                previewContainer.innerHTML = '<span style="color: #f44336; font-size: 11px;">Failed to load image</span>';
                            };
                            img.src = fileUri;
                        } else if (cat === 'decals' || cat === 'icons' || cat === 'skyboxes') {
                            previewContainer.innerHTML = `<img src="${fileUri}" style="max-width: 250px; max-height: 150px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.2);" />`;
                        } else if (cat === 'textures') {
                            const isKtx2 = pathStr && pathStr.toLowerCase().endsWith('.ktx2');
                            if (isKtx2) {
                                // Request snapshot via HTTP REST API from Godot host
                                const ipcPort = new URLSearchParams(window.location.search).get('ipcPort') || '8092';
                                fetch(`http://127.0.0.1:${ipcPort}/api/`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ action: 'generateSnapshot', filePath: pathStr, requestId: reqId })
                                }).then(res => res.json()).then(msg => {
                                    if (msg && msg.base64) {
                                        previewContainer.innerHTML = `<img src="data:image/png;base64,${msg.base64}" style="max-width: 256px; max-height: 256px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.2);" title="Godot Live Snapshot (256x256)" />`;
                                    }
                                }).catch(err => {
                                    console.warn('REST IPC snapshot fetch error:', err);
                                });

                                previewContainer.innerHTML = `
                                    <div style="display:flex; flex-direction:column; align-items:center; gap:8px;">
                                        <span class="snapshot-status-lbl" style="color: var(--text-muted); font-size: 11px; word-break: break-all;">Rendering Godot live snapshot...</span>
                                        <button type="button" class="btn small-btn btn-open-in-vscode" data-path="${escapeHtml(pathStr)}" style="background: rgba(78,201,176,0.15); border: 1px solid rgba(78,201,176,0.4); border-radius: 4px; cursor: pointer; font-size: 11px; padding: 4px 10px; color: #4ec9b0;">🖼️ Open in Texture Viewer Extension</button>
                                    </div>`;
                                previewContainer.querySelector('.btn-open-in-vscode').addEventListener('click', () => {
                                    vscode.postMessage({ type: 'openFile', path: pathStr });
                                });
                            } else {
                                previewContainer.innerHTML = `<img src="${fileUri}" style="max-width: 250px; max-height: 150px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.2);" />`;
                            }
                        } else if (cat === 'sfx' || cat === 'music') {
                            previewContainer.innerHTML = `<audio controls src="${fileUri}" style="width: 100%; max-width: 250px;"></audio>`;
                        } else if (cat === 'glb') {
                            // Request snapshot via HTTP REST API from Godot host
                            const ipcPort = new URLSearchParams(window.location.search).get('ipcPort') || '8092';
                            fetch(`http://127.0.0.1:${ipcPort}/api/`, {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ action: 'generateSnapshot', filePath: pathStr, requestId: reqId })
                            }).then(res => res.json()).then(msg => {
                                if (msg && msg.base64) {
                                    previewContainer.innerHTML = `<img src="data:image/png;base64,${msg.base64}" style="max-width: 256px; max-height: 256px; border-radius: 4px; border: 1px solid rgba(255,255,255,0.2);" title="Godot Live Snapshot (256x256)" />`;
                                }
                            }).catch(err => {
                                console.warn('REST IPC snapshot fetch error:', err);
                            });

                            previewContainer.innerHTML = `
                                <div style="display:flex; flex-direction:column; align-items:center; gap:8px;">
                                    <span class="snapshot-status-lbl" style="color: var(--text-muted); font-size: 11px; word-break: break-all;">Rendering Godot live 3D snapshot...</span>
                                    <button type="button" class="btn small-btn btn-open-in-vscode" data-path="${escapeHtml(pathStr)}" style="background: rgba(120,120,255,0.15); border: 1px solid rgba(120,120,255,0.4); border-radius: 4px; cursor: pointer; font-size: 11px; padding: 4px 10px; color: #9c9cff;">📦 Open in GLB Viewer Extension</button>
                                </div>`;
                            previewContainer.querySelector('.btn-open-in-vscode').addEventListener('click', () => {
                                vscode.postMessage({ type: 'openFile', path: pathStr });
                            });
                        } else {
                            previewContainer.innerHTML = `<span style="color: var(--text-muted); font-size: 11px;">Asset path: ${escapeHtml(fileUri)}</span>`;
                        }
                    }
                };
                window.addEventListener('message', handleResult);
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.addEventListener('message', handleResult);
                }
                vscode.postMessage({
                    type: 'resolvePath',
                    requestId: reqId,
                    path: pathStr
                });
            });
        });
    }

    function setupAssetImportListeners() {
        const btnTexture = document.getElementById('btn-import-texture');
        if (btnTexture) {
            btnTexture.addEventListener('click', () => {
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'texture',
                    options: {}
                });
            });
        }

        const btnGlb = document.getElementById('btn-import-glb');
        if (btnGlb) {
            btnGlb.addEventListener('click', () => {
                const catSelect = document.getElementById('glb-category-select');
                const category = catSelect ? catSelect.value : 'props';
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'glb',
                    options: { category }
                });
            });
        }

        const btnSkybox = document.getElementById('btn-import-skybox');
        if (btnSkybox) {
            btnSkybox.addEventListener('click', () => {
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'skybox'
                });
            });
        }

        const btnDecal = document.getElementById('btn-import-decal');
        if (btnDecal) {
            btnDecal.addEventListener('click', () => {
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'decal'
                });
            });
        }

        const btnIcon = document.getElementById('btn-import-icon');
        if (btnIcon) {
            btnIcon.addEventListener('click', () => {
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'icon'
                });
            });
        }

        const btnVfx = document.getElementById('btn-import-vfx');
        if (btnVfx) {
            btnVfx.addEventListener('click', () => {
                const colsInput = document.getElementById('vfx-cols-input');
                const rowsInput = document.getElementById('vfx-rows-input');
                const columns = colsInput ? parseInt(colsInput.value, 10) || 4 : 4;
                const rows = rowsInput ? parseInt(rowsInput.value, 10) || 4 : 4;
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'vfx',
                    options: { columns, rows }
                });
            });
        }

        const btnAudio = document.getElementById('btn-import-audio');
        if (btnAudio) {
            btnAudio.addEventListener('click', () => {
                const audioSelect = document.getElementById('audio-type-select');
                const audioType = audioSelect ? audioSelect.value : 'sfx';
                vscode.postMessage({
                    type: 'importAsset',
                    assetType: 'audio',
                    options: { audioType }
                });
            });
        }
    }

    // Call setupAssetImportListeners inside init
    const originalInit = init;
    init = function() {
        originalInit();
        setupAssetImportListeners();
    };

    init();
})();
