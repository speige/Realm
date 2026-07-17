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
    const addCustomWeapon5Btn = document.getElementById('add-custom-weapon-5-btn');
    const pasteCustomWeaponBtn = document.getElementById('paste-custom-weapon-btn');

    const customAbilitiesForm = document.getElementById('custom-abilities-form');
    const customAbilitiesList = document.getElementById('custom-abilities-list');
    const addCustomAbilityBtn = document.getElementById('add-custom-ability-btn');
    const addCustomAbility5Btn = document.getElementById('add-custom-ability-5-btn');
    const pasteCustomAbilityBtn = document.getElementById('paste-custom-ability-btn');

    const customUpgradesForm = document.getElementById('custom-upgrades-form');
    const customUpgradesList = document.getElementById('custom-upgrades-list');
    const addCustomUpgradeBtn = document.getElementById('add-custom-upgrade-btn');
    const addCustomUpgrade5Btn = document.getElementById('add-custom-upgrade-5-btn');
    const pasteCustomUpgradeBtn = document.getElementById('paste-custom-upgrade-btn');

    const customItemsForm = document.getElementById('custom-items-form');
    const customItemsList = document.getElementById('custom-items-list');
    const addCustomItemBtn = document.getElementById('add-custom-item-btn');
    const addCustomItem5Btn = document.getElementById('add-custom-item-5-btn');
    const pasteCustomItemBtn = document.getElementById('paste-custom-item-btn');

    const unitListContainer = document.getElementById('unit-list');
    const addUnitBtn = document.getElementById('add-unit-btn');
    const addUnit5Btn = document.getElementById('add-unit-5-btn');
    const searchInput = document.getElementById('search-input');
    const editorTitle = document.getElementById('editor-title');
    const editorSubtitle = document.getElementById('editor-subtitle');

    const toggleDebugBtn = document.getElementById('toggle-debug-btn');
    const toggleLockBtn = document.getElementById('toggle-lock-btn');
    const toggleButtonsBtn = document.getElementById('toggle-buttons-btn');
    const copyJsonBtn = document.getElementById('copy-json-btn');
    const expandJsonBtn = document.getElementById('expand-json-btn');
    const duplicateUnitBtn = document.getElementById('duplicate-unit-btn');
    const copyUnitBtn = document.getElementById('copy-unit-btn');
    const pasteUnitBtn = document.getElementById('paste-unit-btn');

    const formFields = {
        UnitId: document.getElementById('field-UnitId'),
        Name: document.getElementById('field-Name'),
        Description: document.getElementById('field-Description'),
        ModelPath: document.getElementById('field-ModelPath'),
        PortraitModelPath: document.getElementById('field-PortraitModelPath'),
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
        MovementType: document.getElementById('field-MovementType')
    };

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

        // Notify VS Code that frontend is ready
        vscode.postMessage({ type: 'ready' });
    }

    // switchTab handles toggling between domains
    function switchTab(domain) {
        document.querySelectorAll('.tab-btn').forEach(b => {
            b.classList.toggle('active', b.dataset.domain === domain);
        });

        const appContainer = document.querySelector('.app-container');
        if (domain === 'units') {
            appContainer.classList.remove('sidebar-hidden');
        } else {
            appContainer.classList.add('sidebar-hidden');
        }

        if (domain === 'units') {
            if (selectedUnitId && !selectedUnitId.startsWith('__')) {
                selectUnit(selectedUnitId);
            } else {
                const skipKeys = ['MapProperties', 'CustomWeapons', 'CustomAbilities', 'CustomUpgrades', 'CustomItems'];
                const firstUnitId = Object.keys(units).find(id => !skipKeys.includes(id));
                if (firstUnitId) {
                    selectUnit(firstUnitId);
                } else {
                    selectedUnitId = null;
                    showEmptyState();
                }
            }
        } else if (domain === 'weapons') {
            selectCustomWeapons();
        } else if (domain === 'abilities') {
            selectCustomAbilities();
        } else if (domain === 'upgrades') {
            selectCustomUpgrades();
        } else if (domain === 'items') {
            selectCustomItems();
        } else if (domain === 'properties') {
            selectMapProperties();
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
                if (units.CustomWeapons) {
                    units.MapProperties.CustomWeapons = units.CustomWeapons;
                    delete units.CustomWeapons;
                    migrated = true;
                }
                if (units.CustomAbilities) {
                    units.MapProperties.CustomAbilities = units.CustomAbilities;
                    delete units.CustomAbilities;
                    migrated = true;
                }
                if (units.CustomUpgrades) {
                    units.MapProperties.CustomUpgrades = units.CustomUpgrades;
                    delete units.CustomUpgrades;
                    migrated = true;
                }
                if (units.CustomItems) {
                    units.MapProperties.CustomItems = units.CustomItems;
                    delete units.CustomItems;
                    migrated = true;
                }
                if (migrated) {
                    saveChanges();
                }

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
                    } else if (selectedUnitId && units[selectedUnitId]) {
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

        const skipKeys = ['MapProperties', 'CustomWeapons', 'CustomAbilities', 'CustomUpgrades', 'CustomItems'];
        for (const [id, unit] of Object.entries(units)) {
            if (skipKeys.includes(id)) continue;
            if (!unit) continue;
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

            const deleteBtn = document.createElement('button');
            deleteBtn.className = 'delete-card-btn';
            deleteBtn.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M10 11v6M14 11v6"/></svg>`;

            deleteBtn.addEventListener('click', e => {
                e.stopPropagation();
                showDeleteConfirm(name || id, id);
            });

            card.appendChild(header);
            card.appendChild(descDiv);
            if (badges.children.length > 0) {
                card.appendChild(badges);
            }
            card.appendChild(deleteBtn);

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
    }

    function populateForm(id) {
        const unit = units[id];
        if (!unit) {
            showEmptyState();
            return;
        }

        hideAllForms();
        editorForm.classList.remove('hidden');
        editorTitle.textContent = unit.Name || id;
        editorSubtitle.textContent = `ID: ${id}`;
        
        const breadcrumb = document.getElementById('editor-breadcrumb');
        if (breadcrumb) {
            breadcrumb.textContent = `Units > ${id}`;
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

        renderTags('build-options', unit.BuildOptions || []);
        renderTags('abilities', unit.Abilities || []);
        renderTags('weapons', unit.Weapons || []);
        renderTags('items', unit.StartingItems || []);
        renderTags('upgrades', unit.Upgrades || []);
        renderTags('statuseffects', unit.StatusEffects || []);
        renderTags('soundevents', unit.SoundEvents || []);
        
        updateAllThumbnails();
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

        if (!selectedUnitId || !units[selectedUnitId]) return;
        
        let key;
        if (type === 'build-options') key = 'BuildOptions';
        else if (type === 'abilities') key = 'Abilities';
        else if (type === 'weapons') key = 'Weapons';
        else if (type === 'items') key = 'StartingItems';
        else if (type === 'upgrades') key = 'Upgrades';
        else if (type === 'statuseffects') key = 'StatusEffects';
        else if (type === 'soundevents') key = 'SoundEvents';

        if (key && units[selectedUnitId][key]) {
            pushToUndoStack();
            units[selectedUnitId][key].splice(index, 1);
            saveChanges();
            renderTags(type, units[selectedUnitId][key]);
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

        if (!selectedUnitId || !units[selectedUnitId] || !inputEl || !key) return;

        const val = inputEl.value.trim();
        if (val) {
            pushToUndoStack();
            if (!units[selectedUnitId][key]) {
                units[selectedUnitId][key] = [];
            }
            units[selectedUnitId][key].push(val);
            inputEl.value = '';
            saveChanges();
            renderTags(type, units[selectedUnitId][key]);
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
        const list = units.MapProperties.CustomWeapons || [];

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
                    units.MapProperties.CustomWeapons = list;
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

                units.MapProperties.CustomWeapons = list;
                saveChanges();
            });
        });
        updateAllThumbnails();
    }

    // Custom Abilities
    function renderCustomAbilities() {
        customAbilitiesList.innerHTML = '';
        const list = units.MapProperties.CustomAbilities || [];
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
                    units.MapProperties.CustomAbilities = list;
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

                units.MapProperties.CustomAbilities = list;
                saveChanges();
            });
        });
        updateAllThumbnails();
    }

    // Custom Upgrades
    function renderCustomUpgrades() {
        customUpgradesList.innerHTML = '';
        const list = units.MapProperties.CustomUpgrades || [];
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
                    units.MapProperties.CustomUpgrades = list;
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

                units.MapProperties.CustomUpgrades = list;
                saveChanges();
            });
        });
    }

    // Custom Items
    function renderCustomItems() {
        customItemsList.innerHTML = '';
        const list = units.MapProperties.CustomItems || [];
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
                    units.MapProperties.CustomItems = list;
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

                units.MapProperties.CustomItems = list;
                saveChanges();
            });
        });
        updateAllThumbnails();
    }

    // --- INLINE SUB-TABLE HANDLERS (AppliedStatusEffects, AffectedUnitIds, PassiveStatusEffects, GrantedWeapons) ---
    function getSubitemArray(type, parentIndex) {
        if (type === 'AppliedStatusEffects') {
            const item = units.MapProperties.CustomAbilities[parentIndex];
            if (!item.AppliedStatusEffects) item.AppliedStatusEffects = [];
            return item.AppliedStatusEffects;
        } else if (type === 'AffectedUnitIds') {
            const item = units.MapProperties.CustomUpgrades[parentIndex];
            if (!item.AffectedUnitIds) item.AffectedUnitIds = [];
            return item.AffectedUnitIds;
        } else if (type === 'PassiveStatusEffects') {
            const item = units.MapProperties.CustomItems[parentIndex];
            if (!item.PassiveStatusEffects) item.PassiveStatusEffects = [];
            return item.PassiveStatusEffects;
        } else if (type === 'GrantedWeapons') {
            const item = units.MapProperties.CustomItems[parentIndex];
            if (!item.GrantedWeapons) item.GrantedWeapons = [];
            return item.GrantedWeapons;
        }
        return null;
    }

    function saveSubitemArray(type, parentIndex, arr) {
        if (type === 'AppliedStatusEffects') {
            units.MapProperties.CustomAbilities[parentIndex].AppliedStatusEffects = arr;
            renderCustomAbilities();
        } else if (type === 'AffectedUnitIds') {
            units.MapProperties.CustomUpgrades[parentIndex].AffectedUnitIds = arr;
            renderCustomUpgrades();
        } else if (type === 'PassiveStatusEffects') {
            units.MapProperties.CustomItems[parentIndex].PassiveStatusEffects = arr;
            renderCustomItems();
        } else if (type === 'GrantedWeapons') {
            units.MapProperties.CustomItems[parentIndex].GrantedWeapons = arr;
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
        const skipKeys = ['MapProperties', 'CustomWeapons', 'CustomAbilities', 'CustomUpgrades', 'CustomItems'];
        return new Set(Object.keys(units).filter(id => !skipKeys.includes(id)));
    }

    // --- UNIT COPY / PASTE SYSTEM CLIPBOARD ---
    if (copyUnitBtn) {
        copyUnitBtn.addEventListener('click', () => {
            if (!selectedUnitId || !units[selectedUnitId]) return;
            const sourceUnit = units[selectedUnitId];
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
                            "MovementType", "StatusEffects", "SoundEvents", "PortraitModelPath"
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

                        units[nextId] = sanitized;
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
            if (type === 'weapon') itemData = units.MapProperties.CustomWeapons[index];
            else if (type === 'ability') itemData = units.MapProperties.CustomAbilities[index];
            else if (type === 'upgrade') itemData = units.MapProperties.CustomUpgrades[index];
            else if (type === 'item') itemData = units.MapProperties.CustomItems[index];

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
            if (!units.MapProperties.CustomWeapons) units.MapProperties.CustomWeapons = [];
            pasteRowData('weapon', units.MapProperties.CustomWeapons, 'WeaponId', 'Weapon');
        });
    }
    if (pasteCustomAbilityBtn) {
        pasteCustomAbilityBtn.addEventListener('click', () => {
            if (!units.MapProperties.CustomAbilities) units.MapProperties.CustomAbilities = [];
            pasteRowData('ability', units.MapProperties.CustomAbilities, 'AbilityId', 'Ability');
        });
    }
    if (pasteCustomUpgradeBtn) {
        pasteCustomUpgradeBtn.addEventListener('click', () => {
            if (!units.MapProperties.CustomUpgrades) units.MapProperties.CustomUpgrades = [];
            pasteRowData('upgrade', units.MapProperties.CustomUpgrades, 'UpgradeId', 'Upgrade');
        });
    }
    if (pasteCustomItemBtn) {
        pasteCustomItemBtn.addEventListener('click', () => {
            if (!units.MapProperties.CustomItems) units.MapProperties.CustomItems = [];
            pasteRowData('item', units.MapProperties.CustomItems, 'ItemId', 'Item');
        });
    }

    // --- COMPONENT LEVEL COPY/PASTE FOR UNIT FORM ---
    function copyUnitComponent(componentKey) {
        if (!selectedUnitId || !units[selectedUnitId]) return;
        const arr = units[selectedUnitId][componentKey] || [];
        navigator.clipboard.writeText(JSON.stringify({
            "$realm_editor_type": "unit-component-block",
            "componentType": componentKey,
            "data": arr
        }));
    }

    function pasteUnitComponent(componentKey) {
        if (isLocked) return;
        if (!selectedUnitId || !units[selectedUnitId]) return;
        navigator.clipboard.readText().then(text => {
            try {
                const parsed = JSON.parse(text);
                if (parsed && parsed.$realm_editor_type === 'unit-component-block' && Array.isArray(parsed.data)) {
                    pushToUndoStack();
                    units[selectedUnitId][componentKey] = parsed.data;
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
        if (!selectedUnitId || !units[selectedUnitId]) return;
        const sourceUnit = units[selectedUnitId];
        
        const nextId = incrementId(selectedUnitId, getExistingUnitIds());
        
        const newUnit = JSON.parse(JSON.stringify(sourceUnit));
        newUnit.UnitId = nextId;
        newUnit.Name = `${sourceUnit.Name || 'New Unit'} (Copy)`;
        
        units[nextId] = newUnit;
        selectUnit(nextId);
        saveChanges();
    }

    function duplicateWeapon(index) {
        const list = units.MapProperties.CustomWeapons || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(w => w.WeaponId));
        const nextId = incrementId(source.WeaponId, existingIds);
        
        const newWeapon = JSON.parse(JSON.stringify(source));
        newWeapon.WeaponId = nextId;
        newWeapon.Name = `${source.Name || 'New Weapon'} (Copy)`;
        
        list.splice(index + 1, 0, newWeapon);
        units.MapProperties.CustomWeapons = list;
        saveChanges();
        renderCustomWeapons();
    }

    function duplicateAbility(index) {
        const list = units.MapProperties.CustomAbilities || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(a => a.AbilityId));
        const nextId = incrementId(source.AbilityId, existingIds);
        
        const newAbility = JSON.parse(JSON.stringify(source));
        newAbility.AbilityId = nextId;
        newAbility.Name = `${source.Name || 'New Ability'} (Copy)`;
        
        list.splice(index + 1, 0, newAbility);
        units.MapProperties.CustomAbilities = list;
        saveChanges();
        renderCustomAbilities();
    }

    function duplicateUpgrade(index) {
        const list = units.MapProperties.CustomUpgrades || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(u => u.UpgradeId));
        const nextId = incrementId(source.UpgradeId, existingIds);
        
        const newUpgrade = JSON.parse(JSON.stringify(source));
        newUpgrade.UpgradeId = nextId;
        newUpgrade.Name = `${source.Name || 'New Upgrade'} (Copy)`;
        
        list.splice(index + 1, 0, newUpgrade);
        units.MapProperties.CustomUpgrades = list;
        saveChanges();
        renderCustomUpgrades();
    }

    function duplicateItem(index) {
        const list = units.MapProperties.CustomItems || [];
        if (!list[index]) return;
        
        const source = list[index];
        const existingIds = new Set(list.map(i => i.ItemId));
        const nextId = incrementId(source.ItemId, existingIds);
        
        const newItem = JSON.parse(JSON.stringify(source));
        newItem.ItemId = nextId;
        newItem.Name = `${source.Name || 'New Item'} (Copy)`;
        
        list.splice(index + 1, 0, newItem);
        units.MapProperties.CustomItems = list;
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
            delete units[id];
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
        const nextId = generateNextId('Unit', getExistingUnitIds());

        units[nextId] = {
            UnitId: nextId,
            Name: 'New Unit',
            Description: 'A new custom unit.',
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
            ArmorType: 'light',
            GoldBounty: 10.0
        };

        selectUnit(nextId);
        saveChanges();
    });

    addUnit5Btn.addEventListener('click', () => {
        if (isLocked) return;
        pushToUndoStack();
        for (let i = 0; i < 5; i++) {
            const nextId = generateNextId('Unit', getExistingUnitIds());
            units[nextId] = {
                UnitId: nextId,
                Name: 'New Unit',
                Description: 'A new custom unit.',
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
                ArmorType: 'light',
                GoldBounty: 10.0
            };
        }
        renderUnitList();
        saveChanges();
    });

    addCustomWeaponBtn.addEventListener('click', () => {
        if (!units.MapProperties.CustomWeapons) {
            units.MapProperties.CustomWeapons = [];
        }
        const list = units.MapProperties.CustomWeapons;
        const nextId = generateNextId('Weapon', new Set(list.map(w => w.WeaponId)));
        list.push({
            WeaponId: nextId,
            Name: 'New Weapon',
            Damage: 10,
            Range: 2.0,
            AttackCooldown: 1.5,
            AttackType: 'melee'
        });
        units.MapProperties.CustomWeapons = list;
        saveChanges();
        renderCustomWeapons();
    });

    addCustomWeapon5Btn.addEventListener('click', () => {
        if (isLocked) return;
        pushToUndoStack();
        if (!units.MapProperties.CustomWeapons) {
            units.MapProperties.CustomWeapons = [];
        }
        const list = units.MapProperties.CustomWeapons;
        for (let i = 0; i < 5; i++) {
            const nextId = generateNextId('Weapon', new Set(list.map(w => w.WeaponId)));
            list.push({
                WeaponId: nextId,
                Name: 'New Weapon',
                Damage: 10,
                Range: 2.0,
                AttackCooldown: 1.5,
                AttackType: 'melee'
            });
        }
        units.MapProperties.CustomWeapons = list;
        saveChanges();
        renderCustomWeapons();
    });

    addCustomAbilityBtn.addEventListener('click', () => {
        if (!units.MapProperties.CustomAbilities) {
            units.MapProperties.CustomAbilities = [];
        }
        const list = units.MapProperties.CustomAbilities;
        const nextId = generateNextId('Ability', new Set(list.map(a => a.AbilityId)));
        list.push({
            AbilityId: nextId,
            Name: 'New Ability',
            Description: 'A new spell effect.',
            AbilityType: 'target_spell'
        });
        units.MapProperties.CustomAbilities = list;
        saveChanges();
        renderCustomAbilities();
    });

    addCustomAbility5Btn.addEventListener('click', () => {
        if (isLocked) return;
        pushToUndoStack();
        if (!units.MapProperties.CustomAbilities) {
            units.MapProperties.CustomAbilities = [];
        }
        const list = units.MapProperties.CustomAbilities;
        for (let i = 0; i < 5; i++) {
            const nextId = generateNextId('Ability', new Set(list.map(a => a.AbilityId)));
            list.push({
                AbilityId: nextId,
                Name: 'New Ability',
                Description: 'A new spell effect.',
                AbilityType: 'target_spell'
            });
        }
        units.MapProperties.CustomAbilities = list;
        saveChanges();
        renderCustomAbilities();
    });

    addCustomUpgradeBtn.addEventListener('click', () => {
        if (!units.MapProperties.CustomUpgrades) {
            units.MapProperties.CustomUpgrades = [];
        }
        const list = units.MapProperties.CustomUpgrades;
        const nextId = generateNextId('Upgrade', new Set(list.map(u => u.UpgradeId)));
        list.push({
            UpgradeId: nextId,
            Name: 'New Upgrade',
            Description: 'Increases unit stats.'
        });
        units.MapProperties.CustomUpgrades = list;
        saveChanges();
        renderCustomUpgrades();
    });

    addCustomUpgrade5Btn.addEventListener('click', () => {
        if (isLocked) return;
        pushToUndoStack();
        if (!units.MapProperties.CustomUpgrades) {
            units.MapProperties.CustomUpgrades = [];
        }
        const list = units.MapProperties.CustomUpgrades;
        for (let i = 0; i < 5; i++) {
            const nextId = generateNextId('Upgrade', new Set(list.map(u => u.UpgradeId)));
            list.push({
                UpgradeId: nextId,
                Name: 'New Upgrade',
                Description: 'Increases unit stats.'
            });
        }
        units.MapProperties.CustomUpgrades = list;
        saveChanges();
        renderCustomUpgrades();
    });

    addCustomItemBtn.addEventListener('click', () => {
        if (!units.MapProperties.CustomItems) {
            units.MapProperties.CustomItems = [];
        }
        const list = units.MapProperties.CustomItems;
        const nextId = generateNextId('Item', new Set(list.map(i => i.ItemId)));
        list.push({
            ItemId: nextId,
            Name: 'New Item',
            Description: 'A custom inventory item.',
            ItemClass: 'consumable'
        });
        units.MapProperties.CustomItems = list;
        saveChanges();
        renderCustomItems();
    });

    addCustomItem5Btn.addEventListener('click', () => {
        if (isLocked) return;
        pushToUndoStack();
        if (!units.MapProperties.CustomItems) {
            units.MapProperties.CustomItems = [];
        }
        const list = units.MapProperties.CustomItems;
        for (let i = 0; i < 5; i++) {
            const nextId = generateNextId('Item', new Set(list.map(i => i.ItemId)));
            list.push({
                ItemId: nextId,
                Name: 'New Item',
                Description: 'A custom inventory item.',
                ItemClass: 'consumable'
            });
        }
        units.MapProperties.CustomItems = list;
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
            if (el.classList.contains('browse-btn') || el.classList.contains('clear-btn') || el.classList.contains('btn-delete') || el.classList.contains('btn-duplicate-item') || el.classList.contains('remove-tag') || el.classList.contains('btn-delete-subitem') || el.classList.contains('add-subitem-btn') || el.classList.contains('copy-subtable-btn') || el.classList.contains('paste-subtable-btn') || el.classList.contains('copy-row-btn') || el.classList.contains('copy-unit-comp-btn') || el.classList.contains('paste-unit-comp-btn') || el.id === 'duplicate-unit-btn' || el.id === 'copy-unit-btn' || el.id === 'paste-unit-btn') {
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

        const skipKeys = ['MapProperties', 'CustomWeapons', 'CustomAbilities', 'CustomUpgrades', 'CustomItems'];
        const existingUnitIds = new Set(Object.keys(units).filter(id => !skipKeys.includes(id)));
        const props = units.MapProperties || {};
        
        const existingWeaponIds = new Set((props.CustomWeapons || []).map(w => w.WeaponId).filter(Boolean));
        const existingAbilityIds = new Set((props.CustomAbilities || []).map(a => a.AbilityId).filter(Boolean));
        const existingUpgradeIds = new Set((props.CustomUpgrades || []).map(u => u.UpgradeId).filter(Boolean));
        const existingItemIds = new Set((props.CustomItems || []).map(i => i.ItemId).filter(Boolean));

        for (const [id, unit] of Object.entries(units)) {
            if (skipKeys.includes(id)) continue;
            if (!unit) continue;
            
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

        (props.CustomAbilities || []).forEach((item, index) => {
            const abiErrors = {};
            if (item.SummonedUnitId && !existingUnitIds.has(item.SummonedUnitId)) {
                abiErrors['SummonedUnitId'] = `Unit ID "${item.SummonedUnitId}" does not exist.`;
            }
            if (Object.keys(abiErrors).length > 0) {
                errors.abilities[index] = abiErrors;
            }
        });

        (props.CustomUpgrades || []).forEach((item, index) => {
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

        (props.CustomItems || []).forEach((item, index) => {
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
        const props = units.MapProperties || {};
        if (type === 'build-options' || type === 'suggest-units') {
            const u = units[id];
            if (u) {
                return { title: u.Name || id, desc: u.Description || 'No description.' };
            }
        } else if (type === 'weapons' || type === 'suggest-weapons') {
            const w = (props.CustomWeapons || []).find(x => x.WeaponId === id);
            if (w) {
                return { title: w.Name || id, desc: `Damage: ${w.Damage || 0}, Range: ${w.Range || 0}` };
            }
        } else if (type === 'abilities' || type === 'suggest-abilities') {
            const a = (props.CustomAbilities || []).find(x => x.AbilityId === id);
            if (a) {
                return { title: a.Name || id, desc: a.Description || 'No description.' };
            }
        } else if (type === 'items' || type === 'suggest-items') {
            const i = (props.CustomItems || []).find(x => x.ItemId === id);
            if (i) {
                return { title: i.Name || id, desc: i.Description || 'No description.' };
            }
        } else if (type === 'upgrades' || type === 'suggest-upgrades') {
            const u = (props.CustomUpgrades || []).find(x => x.UpgradeId === id);
            if (u) {
                return { title: u.Name || id, desc: u.Description || 'No description.' };
            }
        }
        return null;
    }

    function updateDatalists() {
        const skipKeys = ['MapProperties', 'CustomWeapons', 'CustomAbilities', 'CustomUpgrades', 'CustomItems'];
        const unitIds = Object.keys(units).filter(id => !skipKeys.includes(id) && units[id]);
        const props = units.MapProperties || {};
        const weapons = props.CustomWeapons || [];
        const abilities = props.CustomAbilities || [];
        const upgrades = props.CustomUpgrades || [];
        const items = props.CustomItems || [];

        populateDatalist('suggest-units', unitIds.map(id => ({ id, name: units[id].Name })));
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
            if (!selectedUnitId || !units[selectedUnitId]) return;

            const val = e.target.type === 'checkbox' ? e.target.checked : e.target.value;

            if (key === 'UnitId') {
                const newId = val.trim();
                if (!newId || (newId !== selectedUnitId && units[newId])) {
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
                units[newId] = { ...units[selectedUnitId], UnitId: newId };
                delete units[selectedUnitId];
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
            units[selectedUnitId][key] = parsedVal;
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
        
        const sortedKeys = Object.keys(data).sort();
        const lines = [];
        lines.push('{');
        
        if (data.MapProperties) {
            lines.push('  "MapProperties": {');
            const props = data.MapProperties;
            const propKeys = Object.keys(props).sort();
            
            propKeys.forEach((pKey, pIdx) => {
                const pVal = props[pKey];
                const isLastProp = pIdx === propKeys.length - 1;
                const comma = isLastProp ? '' : ',';
                
                if (pKey === 'CustomWeapons' && Array.isArray(pVal)) {
                    lines.push('    "CustomWeapons": [');
                    const sortedWeapons = [...pVal].sort((a, b) => (a.WeaponId || '').localeCompare(b.WeaponId || ''));
                    sortedWeapons.forEach((w, wIdx) => {
                        const sortedW = sortObjectKeys(w);
                        const wLine = `      ${JSON.stringify(sortedW)}${wIdx === sortedWeapons.length - 1 ? '' : ','}`;
                        lines.push(wLine);
                    });
                    lines.push(`    ]${comma}`);
                } else if (pKey === 'CustomAbilities' && Array.isArray(pVal)) {
                    lines.push('    "CustomAbilities": [');
                    const sortedAbis = [...pVal].sort((a, b) => (a.AbilityId || '').localeCompare(b.AbilityId || ''));
                    sortedAbis.forEach((a, aIdx) => {
                        const sortedA = sortObjectKeys(a);
                        const aLine = `      ${JSON.stringify(sortedA)}${aIdx === sortedAbis.length - 1 ? '' : ','}`;
                        lines.push(aLine);
                    });
                    lines.push(`    ]${comma}`);
                } else if (pKey === 'CustomUpgrades' && Array.isArray(pVal)) {
                    lines.push('    "CustomUpgrades": [');
                    const sortedUpgs = [...pVal].sort((a, b) => (a.UpgradeId || '').localeCompare(b.UpgradeId || ''));
                    sortedUpgs.forEach((u, uIdx) => {
                        const sortedU = sortObjectKeys(u);
                        const uLine = `      ${JSON.stringify(sortedU)}${uIdx === sortedUpgs.length - 1 ? '' : ','}`;
                        lines.push(uLine);
                    });
                    lines.push(`    ]${comma}`);
                } else if (pKey === 'CustomItems' && Array.isArray(pVal)) {
                    lines.push('    "CustomItems": [');
                    const sortedItems = [...pVal].sort((a, b) => (a.ItemId || '').localeCompare(b.ItemId || ''));
                    sortedItems.forEach((item, iIdx) => {
                        const sortedI = sortObjectKeys(item);
                        const iLine = `      ${JSON.stringify(sortedI)}${iIdx === sortedItems.length - 1 ? '' : ','}`;
                        lines.push(iLine);
                    });
                    lines.push(`    ]${comma}`);
                } else if (pKey === 'PlayerSlots' && Array.isArray(pVal)) {
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
            
            const isLastTopKey = sortedKeys.length === 1;
            lines.push(`  }${isLastTopKey ? '' : ','}`);
        }
        
        const unitKeys = sortedKeys.filter(k => k !== 'MapProperties');
        unitKeys.forEach((uKey, uIdx) => {
            const unit = data[uKey];
            const sortedUnit = sortObjectKeys(unit);
            const comma = uIdx === unitKeys.length - 1 ? '' : ',';
            lines.push(`  "${uKey}": ${JSON.stringify(sortedUnit)}${comma}`);
        });
        
        lines.push('}');
        return lines.join('\n');
    }

    function sortObjectKeys(obj) {
        if (obj === null || typeof obj !== 'object' || Array.isArray(obj)) {
            return obj;
        }
        const sorted = {};
        Object.keys(obj).sort().forEach(key => {
            const val = obj[key];
            if (Array.isArray(val)) {
                sorted[key] = val.map(item => {
                    if (typeof item === 'object' && item !== null) {
                        return sortObjectKeys(item);
                    }
                    return item;
                });
            } else if (typeof val === 'object' && val !== null) {
                sorted[key] = sortObjectKeys(val);
            } else {
                sorted[key] = val;
            }
        });
        return sorted;
    }

    // --- RECURSIVE CASCADING (RENAME / DELETE REFERENCES) ---
    function cascadeRename(type, oldId, newId) {
        if (type === 'unit') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.BuildOptions) {
                    unit.BuildOptions = unit.BuildOptions.map(b => b === oldId ? newId : b);
                }
            }
            const props = units.MapProperties || {};
            if (props.CustomAbilities) {
                props.CustomAbilities.forEach(a => {
                    if (a.SummonedUnitId === oldId) a.SummonedUnitId = newId;
                });
            }
            if (props.CustomUpgrades) {
                props.CustomUpgrades.forEach(upg => {
                    if (upg.AffectedUnitIds) {
                        upg.AffectedUnitIds = upg.AffectedUnitIds.map(u => u === oldId ? newId : u);
                    }
                });
            }
        } else if (type === 'weapon') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.Weapons) {
                    unit.Weapons = unit.Weapons.map(w => w === oldId ? newId : w);
                }
            }
            const props = units.MapProperties || {};
            if (props.CustomItems) {
                props.CustomItems.forEach(item => {
                    if (item.GrantedWeapons) {
                        item.GrantedWeapons = item.GrantedWeapons.map(w => w === oldId ? newId : w);
                    }
                });
            }
        } else if (type === 'ability') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.Abilities) {
                    unit.Abilities = unit.Abilities.map(a => a === oldId ? newId : a);
                }
            }
            const props = units.MapProperties || {};
            if (props.CustomItems) {
                props.CustomItems.forEach(item => {
                    if (item.UseAbility === oldId) item.UseAbility = newId;
                });
            }
        } else if (type === 'upgrade') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.Upgrades) {
                    unit.Upgrades = unit.Upgrades.map(u => u === oldId ? newId : u);
                }
            }
        } else if (type === 'item') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.StartingItems) {
                    unit.StartingItems = unit.StartingItems.map(i => i === oldId ? newId : i);
                }
            }
        }
    }

    function cascadeDelete(type, targetId) {
        if (type === 'unit') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.BuildOptions) {
                    unit.BuildOptions = unit.BuildOptions.filter(b => b !== targetId);
                }
            }
            const props = units.MapProperties || {};
            if (props.CustomAbilities) {
                props.CustomAbilities.forEach(a => {
                    if (a.SummonedUnitId === targetId) delete a.SummonedUnitId;
                });
            }
            if (props.CustomUpgrades) {
                props.CustomUpgrades.forEach(upg => {
                    if (upg.AffectedUnitIds) {
                        upg.AffectedUnitIds = upg.AffectedUnitIds.filter(u => u !== targetId);
                    }
                });
            }
        } else if (type === 'weapon') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.Weapons) {
                    unit.Weapons = unit.Weapons.filter(w => w !== targetId);
                }
            }
            const props = units.MapProperties || {};
            if (props.CustomItems) {
                props.CustomItems.forEach(item => {
                    if (item.GrantedWeapons) {
                        item.GrantedWeapons = item.GrantedWeapons.filter(w => w !== targetId);
                    }
                });
            }
        } else if (type === 'ability') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.Abilities) {
                    unit.Abilities = unit.Abilities.filter(a => a !== targetId);
                }
            }
            const props = units.MapProperties || {};
            if (props.CustomItems) {
                props.CustomItems.forEach(item => {
                    if (item.UseAbility === targetId) delete item.UseAbility;
                });
            }
        } else if (type === 'upgrade') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.Upgrades) {
                    unit.Upgrades = unit.Upgrades.filter(u => u !== targetId);
                }
            }
        } else if (type === 'item') {
            for (const [id, unit] of Object.entries(units)) {
                if (id === 'MapProperties') continue;
                if (unit.StartingItems) {
                    unit.StartingItems = unit.StartingItems.filter(i => i !== targetId);
                }
            }
        }
    }

    init();
})();
