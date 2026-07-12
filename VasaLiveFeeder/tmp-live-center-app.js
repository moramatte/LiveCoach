/**
 * live-center-app.js
 * Fix: define safe() (and escHtml()) globally so getStartlistHtml() and others can use it.
 * Also: remove the local safe() inside getTable() so everything uses the same helper.
 *
 * NOTE: I only changed what was required to fix "safe is not defined" + made it safer for HTML.
 */

/* ---------------------------
   Global helpers (NEW)
---------------------------- */
function safe(v) {
  return v == null ? '' : String(v);
}

/**
 * Escapes for HTML text/attribute contexts.
 * Use this when inserting untrusted values into HTML or attributes.
 */
function escHtml(v) {
  return safe(v)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

$( document ).ready(function() {
    jQuery(".load-container").html("");
    
    
    let post_url = jQuery("#theRootUrl").val() + '/v3_live_center/ajax/';

    const urlParams = new URLSearchParams(window.location.search);
    const paramSeason = urlParams.get('season'); // Replace 'paramName' with your actual parameter name
    const paramEvent = urlParams.get('event'); // Replace 'paramName' with your actual parameter name
    const paramGender = urlParams.get('gender'); // Replace 'paramName' with your actual parameter name

    let eventInstance; // top-level, accessible anywhere

    if(paramEvent && paramSeason){

        const startTime = performance.now();
        // const post_url = jQuery("#theRootUrl").val() + '/v3_live_center/ajax/';

        $('#loading').show(); // Show the loading wheel
        eventInstance = new Event(paramEvent, paramSeason, paramGender);

        // --- Main fetch ---
        eventInstance.postData(post_url + 'post_data.php', {
            request: { 
                type: 'information', mode: jQuery("#envMode").val(), base: jQuery("#baseUrl").val()
            },  event_id: eventInstance.id, season: eventInstance.season, gender: eventInstance.gender

        }).then(data => {

            // if(data.isFutureDate === 'Today') jQuery("#scLiveOn").val(1);
            if(data.startType) jQuery("#scStartType").val(data.startType);
            if(data.series_label) jQuery("#scSeries").val(data.series_label);

            const $loadContainer = jQuery(".load-container");
            $loadContainer.append(`<div class="race-btn-wrapper"></div>`);

            // Add races
            if(data.races?.length > 0) {
            
                if(data.eventDate == '20260308'){
                        
                    data.races.forEach(item => {
                        if(item.id == 1548){
                            const label = item.sex === 'M' ? 'Men' : 'Women';
                            $loadContainer.append(createRaceContainer(label, item.id));
                            $loadContainer.find(".race-btn-wrapper").append(createRaceButton(label, item.id));
                            appendRaceContent(`.race-container[data-gender="${label}"]`, item, eventInstance);
                        }
                    });

                } else if(data.eventDate == '20260307'){

                    data.races.forEach(item => {
                        if(item.id == 1549){
                            const label = item.sex === 'M' ? 'Men' : 'Women';
                            $loadContainer.append(createRaceContainer(label, item.id));
                            $loadContainer.find(".race-btn-wrapper").append(createRaceButton(label, item.id));
                            appendRaceContent(`.race-container[data-gender="${label}"]`, item, eventInstance);
                        }
                    }); 

                } else {
                    data.races.forEach(item => {
                        const label = item.sex === 'M' ? 'Men' : 'Women';
                        $loadContainer.append(createRaceContainer(label, item.id));
                        $loadContainer.find(".race-btn-wrapper").append(createRaceButton(label, item.id));
                        appendRaceContent(`.race-container[data-gender="${label}"]`, item, eventInstance);
                    });
                }


            }

            handleGenderUI(paramGender); // Handle gender-specific UI

            if(paramGender){
                
                const $raceId = getRaceIdByGender(paramGender);

                eventInstance.postData(post_url + 'post_data.php', {
                    request: { 
                        type: 'checkpoints', mode: jQuery("#envMode").val(), base: jQuery("#baseUrl").val()
                    },  race_id: $raceId

                }).then(data => {
                    
                    const $raceContainer = jQuery(`.race-container[data-race-id="${$raceId}"]`);

                    // console.log("currently_reached:", data.currently_reached);

                    appendCheckpointsContent($raceContainer, data, eventInstance);                                               
                    loadResults(eventInstance, post_url, $raceId, parseInt(data.currently_reached)); // currently_reached here is the currently reached checkpoint in the race
                    // initLiveCenter(); // initializes the live center    
                    // Performance logging
                    const duration = performance.now() - startTime;
                    // console.log(`First load duration: ${duration.toFixed(3)} ms`);
                });
            } else {
                console.warn("missing gender param...");
                $('#loading').hide();
            }
        });
    } else {
        console.warn("missing params...");
    }

    $(document).on('click', '.checkpoints-container .checkpoint-item', function() {
        $('#loading').show(); // Show the loading wheel
        const checkpointId = $(this).data('checkpoint-id');
        const raceId = $(this).parent().parent().data('race-id');
        loadResults(eventInstance, post_url, raceId, checkpointId);        

        
    });

    $(document).on('click', 'button.gender-btn', function() {
        switch (jQuery(this).attr("data-gender")) {
            case 'Women':
                reloadIfNewParam("gender", "women");
                break;

            case 'Men':
                reloadIfNewParam("gender", "men");
                break;

            default:
                break;
        } 
    });


});

function reloadIfNewParam(param, newValue) {
    let url = new URL(window.location.href);
    let params = new URLSearchParams(url.search);

    // Get the current value of the param
    let currentValue = params.get(param);

    // Only proceed if the value is different from the current one
    if (currentValue !== newValue) {
        
        // Set the new value, whether the param exists or not
        params.set(param, newValue);

        // Construct the new URL
        let newUrl = url.pathname + "?" + params.toString();

        // Update the URL and reload the page if the URL has changed
        window.history.replaceState(null, "", newUrl);
        window.location.reload();
    }   
}

function createRaceContainer(label, id) {
    return `<div class="race-container hide" data-gender="${label}" data-race-id="${id}"></div>`;
}

function createRaceButton(label, id) {
    return `<button class="btn gender-btn" data-gender="${label}" data-race-id="${id}">${label}</button>`;
}

function appendRaceContent(containerSelector, item, event) {
    const $container = jQuery(containerSelector);
    $container.append(event.getHTML('links', item));
    $container.append(event.getHTML('details', item));
}

function loadResults(event, post_url, raceId, chkp_id){
    
    const startTime = performance.now();
    event.postData(post_url + 'post_data.php', {
        request: { 
            type: 'results', mode: jQuery("#envMode").val(), base: jQuery("#baseUrl").val()
        },  race_id: raceId, checkpoint: chkp_id, start_type: jQuery("#scStartType").val(), event_id: event.id

    }).then(data => {

        // if(jQuery("#scStartType").val() == 'sprint'){
        const divs = document.querySelectorAll('div.checkpoints-container div.checkpoint-item');
        if(data.currently_reached && data.currently_reached != 1){
            for (const div of divs) {
                if(div.dataset.checkpointId == data.currently_reached){
                    div.dataset.checkpointActive = 'true';
                    jQuery(div).find("h4 svg").remove();                        
                    jQuery(div).find('h4').append(
                    '<i class="fa-duotone fa-signal-stream fa-fade" aria-hidden="true"></i>'
                    );
                    // console.log("added icon NOT 1");
                } else {
                    div.dataset.checkpointActive = 'false';
                    jQuery(div).find("h4 svg").remove();
                }               
            }
        } 
        // }



        $(document).find(`.load-container .race-container[data-race-id='${raceId}'] .results-container`).html('');

        if(data.results_count == 0){
            

            if(data.race_id == "1548"){
            $(document).find(`.race-container[data-race-id='${raceId}'] .results-container`)
            .html('<a href="https://skiclassics.com/wp-content/uploads/2025/05/09-Orsa-Gronklitt-ITT-Men-1.pdf" style="color:unset;">Startlist PDF</a>');
            // .html('<p class="error">No '+(chkp_id == 1 ? 'startlist' : 'results')+' available.</p>');

            } else {
            $(document).find(`.race-container[data-race-id='${raceId}'] .results-container`)
            .html('<p class="error">No '+(chkp_id == 1 ? 'startlist' : 'results')+' available.</p>');

            }

        } else {
            $(document).find(`.load-container .race-container[data-race-id='${raceId}'] .results-container`).append(event.getHTML('results', data));

            const missing_table_divs = document.querySelectorAll('div.missing-table');
            for (const missing_div of missing_table_divs) {
                const $h3 = prevH3SameLevel(missing_div);                
                
                missing_numbs = jQuery(missing_div).next('table').find('tbody tr').length;
                finished_numbs = jQuery(missing_div).prev('table').find('tbody tr').length;
                total_numbs = missing_numbs + finished_numbs;                
                
                if ($h3 && missing_numbs > 0) {
                    $h3.append(' ('+finished_numbs+'/'+total_numbs+')');
                }
            }

        }

        $(document).find('.load-container .race-container').each(function(index, element){
            if( jQuery(element).hasClass("active")){
                jQuery(element).removeClass("active");
            } 
        });
        $(document).find(`.load-container .race-container[data-race-id='${raceId}']`).addClass("active");

        const duration = performance.now() - startTime;
        // console.log(`load_duration: ${duration.toFixed(3)} ms`);        
        $('#loading').hide(); // Show the loading wheel
        // resetLiveTimer(true); // reset timer and fetch immediately
        
    })
    .catch(err => {
        console.error('Error loading results:', err);
        $('#loading').hide();
        $(document).find(`.race-container[data-race-id='${raceId}'] .results-container`)
        .html('<p class="error">Failed to load results. Please try again.</p>');
    });    
}

function appendCheckpointsContent(containerSelector, item, event) {
    const $container = jQuery(containerSelector);
    $container.append(event.getHTML('checkpoints', item));
    $container.append('<div class="results-container"></div>');
}

function handleGenderUI(paramGender) {
    if(!paramGender) {
        jQuery('.race-btn-wrapper button').css({ "min-height": "100px", "font-size": "30px" });
        jQuery('.race-btn-wrapper').css({ "margin-top": "32px" });
    } 
}

function getRaceIdByGender(paramGender) {
    if(!paramGender) return null;

    const label = paramGender === 'men' ? 'Men' : 'Women';
    const $container = jQuery(`.race-container[data-gender="${label}"]`);

    if($container.length) {
        return $container.data('race-id'); // jQuery .data() reads data-race-id
    }

    return null; // not found
}

function getCheckpointBrand(item) {
    if (item.type === 'FINISH') return 'Finish';
    if (item.type === 'SPLIT') {
        if (item.isClimb) return 'Climb';
        if (item.isSprint) return 'Sprint';
        return 'Timing';
    }
    return '';
}



class Event {

    constructor(event_id, season, gender) {
        this.id = event_id;
        this.season = season;
        this.gender = gender;
    }

    async postData(url, postdata) {
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ postdata })
            });

            const data = await response.json();
            // $('#loading').hide(); // Show the loading wheel
            return data;

        } catch (err) {
            $('#loading').hide(); // Show the loading wheel
            console.error('Error:', err);
            throw err; // ensures .catch() works

        }
    }


    getHTML(type, item) {
        let html = '';
        switch (type) {
            case 'results':
                switch (item.checkpoint_type) {
                    case 'startlist':
                        if(item.startlist){
                            html += getStartlistHtml(item);
                        } else {                
                            html += '<p class="error">No startlist available.</p>';
                        }
                        
                        break;
                    case 'regular':
                        html += getResultsHtml(item);

                        break;
                    default:
                        break;
                }                             
                break;

            case 'checkpoints':
                html += `<div class="checkpoints-container"><h3>Checkpoints</h3>
                <div class="checkpoint-item" data-checkpoint-id="1"${item.currently_reached == 1 ? ` data-checkpoint-active="true"` : ''}><h4 class="unselectable">Startlist</h4></div>`;
                item.checkpoints.forEach(function(chkp, index){
                    const brand = getCheckpointBrand(chkp);
                    const isActive = item.currently_reached === chkp.id;
                    const activeAttr = isActive ? ' data-checkpoint-active="true"' : '';
                    // const activeIcon = isActive ? '<i class="fa-duotone fa-signal-stream fa-fade"></i>' : '';
                    const activeIcon = isActive ? '' : '';

                    html += `
                        <div class="checkpoint-item ${brand}"
                            data-checkpoint-special="${brand}"
                            data-checkpoint-id="${chkp.id}"${activeAttr}>
                            <h4 class="unselectable">
                                ${chkp.dist} | ${brand} ${activeIcon}
                            </h4>
                        </div>
                    `;
                });
                html += `</div>`;
                break;

            case 'links':
                
                html += `<div class="links-container">                
                    ${item.links.scplay ? `<a class="scplay" href="${item.links.scplay}" target="_blank">Stream on SC PLAY</a>` : ''}
                    ${item.links.gps ? `<a class="gpstracker" href="${item.links.gps}" target="_blank">GPS Tracker</a>` : ''}
                    ${(item.id === "1548" && item.links.start_list_men) 
                        ? `<a class="startlist-men" href="${item.links.start_list_men}" target="_blank">Start list men</a>` 
                        : ''}
                </div>`;

                break;

            case 'details':
                html += `<div class="details-container">
                ${item.title ? `<h1>${item.title} | <span class="gender-label">${item.gender_label}</span></h1>` : ''}
                ${item.series_label ? `<span class="details-label series-label">Ski Classics<span class="series"> | ${item.series_label}</span></span>` : ''}
                ${item.date ? `<span class="details-label date-label">Date<span class="date"> | ${item.date}</span></span>` : ''}
                ${item.adjustedTime ? `<span class="details-label starttime-label">Start time<span class="starttime"> | ${item.adjustedTime}</span></span>` : ''}
                ${item.startType == "sprint" ? `<a class="sprint-diagram" target="_blank" href="https://skiclassics.com/wp-content/themes/skiclassics/v3_live_center/SPRINT_DIAGRAM_with_times.png">Open Sprint Diagram (illustration in link)</a>` : ''}
                </div>`;
                break;
        
            default:
                break;
        }
        return html;
    }
}


function getStartlistHtml(items){
    const startlist = items.startlist;
    let start_times = [];
    let pro_teams = [];
    startlist.forEach(function(item, index) { 
        if(item.startTime){
            start_times.push({
                [item.id]: item.startTime
            });            
        }
        if(item.teamAbb){
            pro_teams.push({
                [item.id]: item.teamAbb
            });            
        }

    });
    // console.log("Start_times:", start_times.length);
    let htmlString = '';
    let table_name = '';
    let table_id = '';
    switch (jQuery("#scStartType").val()) {
        case 'itt':
            const is_itt = true;
            table_name = 'itt-type-table';
            table_id = 'ittTable';
            break;

        case 'team':
        case 'teamtempo':       
            const is_ptt = true;     
            table_name = 'ptt-type-table';
            table_id = 'pttTable';
            break;

        case 'sprint':
            const is_sprint = true;
            table_name = 'sprint-type-table';
            table_id = 'sprintTable';
            break;

        default:
            const is_mass = true;
            table_name = 'mass-type-table';
            table_id = 'massTable';
            break;
    }
    htmlString += `<h3>Startlist</h3>
    <table class="live-center-table startlist-table startlist-${table_name}" id="${table_id}" race-id="${items.race_id}">
    <thead><tr>
    <th>Bib</th><th>Athlete</th><th></th>`;

    if(pro_teams?.length){
        htmlString += `<th>Team</th>`;
    }
    if(start_times?.length){
        htmlString += `<th>Start</th>`;
    }
    htmlString += `</tr></thead>`;
    htmlString += `<tbody>`;
    startlist.forEach(function(compitem, index) { 
        htmlString += `<tr athlete-id="${escHtml(compitem.id)}">
        <td${compitem.colorBib ? ` data-color="${escHtml(compitem.colorBib)}"` : ''}>${compitem.number ? escHtml(compitem.number) : ''}</td>`;
        let name = compitem.name.trim() +" "+compitem.surname.trim();

        const first = (compitem.name || "").trim();
        const last  = (compitem.surname || "").trim();

        let shortName = first
        ? first.charAt(0) + ". " + last
        : last;

        var skier_link = getPostURL('scskier', compitem, items);
        htmlString += `<td>${
        skier_link 
            ? `<a class="name" href="${escHtml(skier_link)}">${escHtml(name.toUpperCase())}</a><a class="nameAbb" href="${escHtml(skier_link)}">${escHtml(shortName.toUpperCase())}</a>` 
            : `<span class="name">${escHtml(name.toUpperCase())}</span><span class="nameAbb">${escHtml(shortName.toUpperCase())}</span>`
        }</td>`;
        htmlString += `<td><span class="${compitem.country ? displayFlag(compitem.country) : ''}"></span></td>`;

        if(pro_teams?.length){
            var team_link = getPostURL('scproteams', compitem, items);
            htmlString += `<td>${
                compitem.teamAbb ? 
                    (team_link ? `<a class="team" href="${escHtml(team_link)}">${escHtml(compitem.team.trim().toUpperCase())}</a><a class="teamAbb" href="${escHtml(team_link)}">${escHtml(compitem.teamAbb)}</a>` : `<span class="team">${escHtml(compitem.team.trim().toUpperCase())}</span><span class="teamAbb">${escHtml(compitem.teamAbb)}</span>`) 
                    : ''
            }</td>`;            
        }

        if(start_times?.length){
            htmlString += `<td>${(compitem.DNS ? 'DNS' : (compitem.startTime ? escHtml(compitem.startTime.split('.')[0]) : ''))}</td>`;        
        }
        htmlString += `</tr>`;
    });
    htmlString += `</tbody></table>`;
    htmlString += '<a href="#top" class="top">Back to top</a>';
    return htmlString;
}


function getPostURL(cpt, item, items){
    let base_url = jQuery("#theBaseUrl").val();
    switch (cpt) {
        case 'scskier':
            if(item.fis){
                if (items.scskiers.includes(item.fis)) { // Check if it's in the array
                    return `${base_url}/skiers/${item.fis}/`;
                }
            }
            break;

        case 'scproteams':
            if (item.abb) { // Make sure teamAbb exists
                if (items.scproteams.includes(item.abb.toLowerCase())) { // Check if it's in the array
                    return `${base_url}/pro-teams/${item.abb}/`;
                }
            } else if(item.teamAbb){
                if (items.scproteams.includes(item.teamAbb.toLowerCase())) { // Check if it's in the array
                    return `${base_url}/pro-teams/${item.teamAbb}/`;
                }                
            }
            break;
    
        default:
            break;
    }

    return false;
}


function getTableLinks(startType, items){
    let htmlString = '';       

    switch (startType) {
        case 'team':
        case 'teamtempo':            
            htmlString += `<div class="table-view-link-wrapper">`;
            htmlString += `<a href="#teamTable">Team table</a>`;
            htmlString += `<a href="#${items.race_id}Table">Individual athletes table</a>`;                
            htmlString += `</div>`;
            break;
    
        case 'sprint':
            if(items.sprint.length > 0){

                htmlString += `<div class="table-view-link-wrapper">`;


                items.sprint.forEach(function (item) {
                let skipItem = false;
                

                item.Checkpoint.forEach(function (chkpitem) {
                    if (skipItem) return; // item already decided as skipped

                    if (!(chkpitem.id && chkpitem.id == items.checkpoint_id)) return;                  

                    const hasCheckpointCompetitors =
                    Array.isArray(chkpitem.Competitor) && chkpitem.Competitor.length > 0;
                    
                    const hasStartlistCompetitors =
                    Array.isArray(items.sprint_startlists) &&
                    items.sprint_startlists.some(function (strtitem) {
                        return (
                        strtitem.phase === item.phase &&
                        Array.isArray(strtitem.Competitor) &&
                        strtitem.Competitor.length > 0
                        );
                    });

                    let spanTime = '';
                    items.sprint_startlists.forEach(function (strtitem) {
                        if(strtitem.phase === item.phase){
                            spanTime = strtitem.adjustedTime;
                        }
                    });

                    // If neither has competitors, skip this entire item
                    if (!hasCheckpointCompetitors && !hasStartlistCompetitors) {
                        htmlString += `<span class="">${item.phase} (${spanTime})</span>`;
                        skipItem = true;
                        return;
                    }
                });

                if (skipItem) return;
                });

                htmlString += `<br>`;

                items.sprint.forEach(function (item) {
                    if(item.phase == 'Qualification' && item.note.length > 0){
                        htmlString += `<p class="subrace-explainer-p">${item.note}</p>`;
                    }
                });                

                items.sprint.forEach(function (item) {
                let skipItem = false;
                
                item.Checkpoint.forEach(function (chkpitem) {
                    if (skipItem) return;

                    if (!(chkpitem.id && chkpitem.id == items.checkpoint_id)) return;                  

                    const hasCheckpointCompetitors =
                    Array.isArray(chkpitem.Competitor) && chkpitem.Competitor.length > 0;
                    
                    const hasStartlistCompetitors =
                    Array.isArray(items.sprint_startlists) &&
                    items.sprint_startlists.some(function (strtitem) {
                        return (
                        strtitem.phase === item.phase &&
                        Array.isArray(strtitem.Competitor) &&
                        strtitem.Competitor.length > 0
                        );
                    });

                    let spanTime = '';
                    items.sprint_startlists.forEach(function (strtitem) {
                        if(strtitem.phase === item.phase){
                            spanTime = strtitem.adjustedTime;
                        }
                    });

                    if (!hasCheckpointCompetitors && !hasStartlistCompetitors) {
                        skipItem = true;
                        return;
                    }

                    htmlString += `<a href="#${item.phase}-h3">${item.phase}</a>`;
                });

                if (skipItem) return;
                });

                htmlString += `<br><a href="#${items.race_id}Table-h3">Full sprint results</a>`;
                
                htmlString += `</div>`;
            }
            break;

        default:
            break;
    }
 
    return htmlString;
}

function getHeatStartlist(ident, items, startlists, phase){
    let htmlString = '';

    startlists.forEach(function(item, index) { 
            if(item.phase && item.phase == phase){

                if (!Array.isArray(item.Competitor) || item.Competitor.length === 0) {
                    return;
                }

                htmlString += `<h3 class="tableh3" id="${item.phase}-h3">`+item.phase+` STARTLIST (${item.adjustedTime})</h3>`;
                htmlString += `<table class="live-center-table sprint-type-table ${ident}-table" id="${item.phase}">
                    <thead><tr>
                    <th></th><th>Bib</th><th>Athlete</th><th></th><th>Team</th>`;
                    htmlString += `</tr></thead>`;
                    htmlString += `<tbody>`;
                    item.Competitor.forEach(function(compitem, compindex) { 
                    
                        htmlString += `<tr>
                        <td></td>
                        <td${compitem.colorBib ? ` data-color="${escHtml(compitem.colorBib)}"` : ''}>${compitem.number ? escHtml(compitem.number) : ''}</td>`;
                        let name = compitem.name.trim() +" "+compitem.surname.trim();

                        const first = (compitem.name || "").trim();
                        const last  = (compitem.surname || "").trim();

                        let shortName = first
                        ? first.charAt(0) + ". " + last
                        : last;

                        var skier_link = getPostURL('scskier', compitem, items);
                        htmlString += `<td>${
                        skier_link 
                            ? `<a class="name" href="${escHtml(skier_link)}">${escHtml(name.toUpperCase())}</a><a class="nameAbb" href="${escHtml(skier_link)}">${escHtml(shortName.toUpperCase())}</a>` 
                            : `<span class="name">${escHtml(name.toUpperCase())}</span><span class="nameAbb">${escHtml(shortName.toUpperCase())}</span>`
                        }</td>`;
                        htmlString += `<td><span class="${compitem.country ? displayFlag(compitem.country) : ''}"></span></td>`;
                        var team_link = getPostURL('scproteams', compitem, items);
                        htmlString += `<td>${
                            compitem.team ? 
                                (team_link ? `<a class="team" href="${escHtml(team_link)}">${escHtml(compitem.team.trim().toUpperCase())}</a><a class="teamAbb" href="${escHtml(team_link)}">${escHtml(compitem.teamAbb)}</a>` : `<span class="team">${escHtml(compitem.team.trim().toUpperCase())}</span><span class="teamAbb">${escHtml(compitem.teamAbb)}</span>`) 
                                : ''
                        }</td>`;
                        
                        htmlString += `</tr>`;
                    
                    });                  
                                    
                    htmlString += `</tbody></table>`;      
            }
    });    

    return htmlString;
}


function getMissingTable(ident, items, missing){
    let htmlString = '';

    switch (ident) {

        case 'sprintHeatTablesMissing':
            const remaining = missing.filter(compitem => !compitem.DNS);
            const remainingCount = remaining.length;           

            if(remainingCount != 0){

                htmlString += `<div class="missing-table"><p class="">${remainingCount} athletes have not yet finished:</p></div>`;
                htmlString += `<table class="live-center-table sprint-type-table ${ident}-table">`;
                
                htmlString += `<tbody>`;
                remaining.forEach(function(compitem, compindex) { 
                                
                    htmlString += `<tr class="">
                    <td>${compitem.rank ? escHtml(compitem.rank) : ''}</td>
                    <td>${compitem.number ? escHtml(compitem.number) : ''}</td>`;
                    let name = compitem.name.trim() +" "+compitem.surname.trim();

                    const first = (compitem.name || "").trim();
                    const last  = (compitem.surname || "").trim();

                    let shortName = first
                    ? first.charAt(0) + ". " + last
                    : last;

                    var skier_link = getPostURL('scskier', compitem, items);
                    htmlString += `<td class="td-skiername">${
                    skier_link 
                        ? `<a class="name" href="${escHtml(skier_link)}">${escHtml(name.toUpperCase())}</a><a class="nameAbb" href="${escHtml(skier_link)}">${escHtml(shortName.toUpperCase())}</a>` 
                        : `<span class="name">${escHtml(name.toUpperCase())}</span><span class="nameAbb">${escHtml(shortName.toUpperCase())}</span>`
                    }</td>`;
                    htmlString += `<td><span class="${compitem.country ? displayFlag(compitem.country) : ''}"></span></td>`;
                    var team_link = getPostURL('scproteams', compitem, items);
                    htmlString += `<td>${
                        compitem.team ? 
                            (team_link ? `<a class="team" href="${escHtml(team_link)}">${escHtml(compitem.team.trim().toUpperCase())}</a><a class="teamAbb" href="${escHtml(team_link)}">${escHtml(compitem.teamAbb)}</a>` : `<span class="team">${escHtml(compitem.team.trim().toUpperCase())}</span><span class="teamAbb">${escHtml(compitem.teamAbb)}</span>`) 
                            : ''
                    }</td>`;

                    const time = compitem.startTime
                        ? compitem.startTime
                            .split('.')[0]
                            .replace(/^0:/, '')
                        : '';

                    htmlString += `<td class="time-td"><span></span>${escHtml(time)}</td>`;
                    htmlString += `</tr>`;
                    
                });                  
                                
                htmlString += `</tbody></table>`;      

            }
            
            break;

    }

    return htmlString;

}



function getTable(ident, items) {
  // Build HTML via array push + join (faster/cleaner than repeated +=)
  const out = [];
  let chkp_label = '';

  // Resolve results source once
  const results =
    items?.start_type === 'sprint'
      ? (items?.full_table?.Competitor ?? [])
      : (items?.results ?? []);

  // Pre-index points/teams by athlete id for O(1) lookups
  const champPtsById = new Map();
  const sprintPtsById = new Map();
  const climbPtsById = new Map();
  const teamAbbById = new Map();

  for (const r of results) {
    const id = r?.id;
    if (!id) continue;

    if (r.champPts != null && r.champPts !== '') champPtsById.set(id, r.champPts);
    if (r.sprintPts != null && r.sprintPts !== '') sprintPtsById.set(id, r.sprintPts);
    if (r.climbPts != null && r.climbPts !== '') climbPtsById.set(id, r.climbPts);
    if (r.teamAbb) teamAbbById.set(id, r.teamAbb);
  }

  const hasChampionCol = champPtsById.size > 0;
  const hasSprintCol = sprintPtsById.size > 0;
  const hasClimbCol = climbPtsById.size > 0;
  const hasProTeams = teamAbbById.size > 0;

  // Helpers
  const upperTrim = (v) => safe(v).trim().toUpperCase();

  const formatTime = (timeStr) => {
    const t = safe(timeStr);
    // remove leading 0: from 0:MM:SS.xx
    return t ? t.replace(/^0:(\d{2}:\d{2}\.\d+)$/, '$1') : '';
  };

  const renderAthleteNameCell = (compitem) => {
    const name = `${safe(compitem?.name).trim()} ${safe(compitem?.surname).trim()}`.trim();
    const first = (compitem.name || "").trim();
    const last  = (compitem.surname || "").trim();

    let shortName = first
    ? first.charAt(0) + ". " + last
    : last;

    const skier_link = getPostURL('scskier', compitem, items);
    return skier_link
  ? `<a class="name" href="${escHtml(skier_link)}">${escHtml(name.toUpperCase())}</a>
     <a class="nameAbb" href="${escHtml(skier_link)}">${escHtml(shortName.toUpperCase())}</a>`
  : `<span class="name">${escHtml(name.toUpperCase())}</span>
     <span class="nameAbb">${escHtml(shortName.toUpperCase())}</span>`;
  };

  const renderTeamCell = (compitem) => {
    if (!compitem?.team) return '';
    const team_link = getPostURL('scproteams', compitem, items);
    const team = upperTrim(compitem.team);
    const teamAbb = compitem.teamAbb;
    return team_link ? `<a class="team" href="${escHtml(team_link)}">${escHtml(team)}</a><a class="teamAbb" href="${escHtml(team_link)}">${escHtml(teamAbb)}</a>` : `<span class="team">${escHtml(team)}</span><span class="teamAbb">${escHtml(teamAbb)}</span>`;
  };

  const renderHeaderRow = (opts) => {
    const {
      includeProTeam = false,
      includeTime = false,
      includeChampion = false,
      includeSprint = false,
      includeClimb = false,
    } = opts;

    const ths = [
      '<th>Rank</th>',
      '<th>Bib</th>',
      '<th>Athlete</th>',
      '<th></th>',
    ];

    if (includeProTeam) ths.push('<th>Team</th>');
    if (includeTime) ths.push('<th class="time-th"><span></span>Time</th>');
    if (includeChampion) ths.push('<th class="champion-th">Champion</th>');
    if (includeSprint) ths.push('<th class="sprint-th">Sprint</th>');
    if (includeClimb) ths.push('<th class="climb-th">Climb</th>');

    return `<thead><tr>${ths.join('')}</tr></thead>`;
  };

  const openTable = (classes, id, headerHtml) => {
    out.push(`<table class="${classes}" id="${id}">`);
    out.push(headerHtml);
    out.push('<tbody>');
  };

  const closeTable = () => {
    out.push('</tbody></table>');
  };

  switch (ident) {
    case 'sprintHeatTablesMissing': {
      out.push(`<p class="">There are ${items.length} athletes left.</p>`);
      break;
    }

    case 'sprintHeatTables': {
        
      const processedByPhase = new Map(); // phase -> array of processed ids

      const startlists = Array.isArray(items?.sprint_startlists) ? items.sprint_startlists : [];

      for (const sprintItem of (items?.sprint ?? [])) {
        for (const chkpitem of (sprintItem?.Checkpoint ?? [])) {
          if (!(chkpitem?.id && chkpitem.id == items.checkpoint_id)) continue;

          const phase = safe(sprintItem?.phase);
          
          if (!processedByPhase.has(phase)) processedByPhase.set(phase, []);

          if (!Array.isArray(chkpitem.Competitor) || chkpitem.Competitor.length === 0) {
            out.push(getHeatStartlist(ident, items, startlists, phase));
            continue;
          }

          out.push(`<h3 class="tableh3" id="${escHtml(phase)}-h3">${escHtml(phase)} RESULTS</h3>`);

          openTable(
            `live-center-table sprint-type-table ${ident}-table`,
            phase,
            renderHeaderRow({ includeProTeam: true, includeTime: true })
          );

          const processedIds = processedByPhase.get(phase);
                
          const cutOff = safe(sprintItem?.skiersToQualify);

          for (const compitem of chkpitem.Competitor) {

            let trClass = '';
            
            processedIds.push(compitem.id);

            if(cutOff && cutOff == compitem.rank){
               trClass = ` class="overunder"`;
            }

            const time_output = formatTime(compitem.time);

            let timeCellValue = '';

            if (compitem.timeDiff) {
                timeCellValue = `+ ${compitem.timeDiff}`;
            } else if (time_output) {
                timeCellValue = time_output;
            } else if (compitem.state === 'DSQ' || compitem.state === 'DNF') {
                timeCellValue = compitem.state;
            }

            let timeTd = `<td>${escHtml(timeCellValue)}</td>`;

            if (compitem.note) {
            timeTd = `
                <td class="time-td">
                <span class="sprint-note-marker">${escHtml(compitem.note)}</span>
                ${escHtml(timeCellValue)}
                </td>
            `;
            } else{
            timeTd = `
                <td class="time-td"><span></span>   
                ${escHtml(timeCellValue)}
                </td>
            `;                
            }

            out.push(
                `<tr${phase !== 'Final' ? trClass : ''}>` +
                `<td>${escHtml(compitem.rank)}</td>` +
                `<td>${escHtml(compitem.number)}</td>` +
                `<td class="td-skiername">${renderAthleteNameCell(compitem)}</td>` +
                `<td><span class="${compitem.country ? displayFlag(compitem.country) : ''}"></span></td>` +
                `<td>${renderTeamCell(compitem)}</td>` +
                timeTd +
              `</tr>`
            );
          }

          closeTable();

          const idSet = new Set(processedIds);

          for (const strtitem of startlists) {
            if (strtitem?.phase !== phase) continue;
            if (!Array.isArray(strtitem.Competitor) || strtitem.Competitor.length === 0) continue;

            const athl_diff = strtitem.Competitor.length - processedIds.length;
            if (athl_diff > 0) {
              strtitem.Competitor = strtitem.Competitor.filter((c) => !idSet.has(c.id));
              strtitem.Competitor = strtitem.Competitor.filter(c => !c.DNS);        
              out.push(getMissingTable('sprintHeatTablesMissing', items, strtitem.Competitor));
            }
          }
        }
      }

      break;
    }

    case 'normal': {
      if (items?.start_type === 'sprint') {
        out.push(`<h3 class="tableh3" id="${escHtml(items.race_id)}Table-h3">Full sprint results</h3>`);
        if (items.race_over === false) {
            if(items.full_table.note){
                out.push(`<p class="subrace-explainer-p">${escHtml(items.full_table.note)}</p>`);
            }
        }
        
      } else if (items?.checkpoint_id) {
        chkp_label = jQuery(`.checkpoint-item[data-checkpoint-id="${items.checkpoint_id}"]`)
          .find('h4')
          .html();
        out.push(`<h3>${chkp_label}</h3>`);
      }

      if (items.race_over !== false) {
        out.push(
            `<table class="live-center-table ${ident}-table" id="${escHtml(items.race_id)}Table" race-id="${escHtml(items.race_id)}">`
        );

        out.push(
            renderHeaderRow({
            includeProTeam: hasProTeams,
            includeTime: items?.start_type !== 'sprint',
            includeChampion: hasChampionCol,
            includeSprint: hasSprintCol,
            includeClimb: hasClimbCol,
            })
        );

        out.push('<tbody>');

        for (const compitem of results) {
            const id = compitem?.id;
            out.push(`<tr athlete-id="${escHtml(id)}">`);
            out.push(`<td>${escHtml(compitem.rank)}</td><td${compitem.colorBib ? ` data-color="${escHtml(compitem.colorBib)}"` : ''}>${escHtml(compitem.number)}</td>`);
            out.push(`<td>${renderAthleteNameCell(compitem)}</td>`);
            out.push(`<td><span class="${compitem.country ? displayFlag(compitem.country) : ''}"></span></td>`);

            if (hasProTeams) {
            if (id && teamAbbById.has(id)) {
                out.push(`<td>${renderTeamCell(compitem)}</td>`);
            } else {
                out.push('<td></td>');
            }
            }

            if (items?.start_type !== 'sprint') {
            out.push(`<td>${compitem.timeDiff ? `+ ${escHtml(compitem.timeDiff)}` : escHtml(compitem.time)}</td>`);
            }

            if (hasChampionCol) out.push(`<td class="champion_points">${id && champPtsById.has(id) ? escHtml(champPtsById.get(id)) : ''}</td>`);
            if (hasSprintCol) out.push(`<td class="sprint_points">${id && sprintPtsById.has(id) ? escHtml(sprintPtsById.get(id)) : ''}</td>`);
            if (hasClimbCol) out.push(`<td class="climb_points">${id && climbPtsById.has(id) ? escHtml(climbPtsById.get(id)) : ''}</td>`);

            out.push('</tr>');
        }

        out.push('</tbody></table>');
      }

      break;
    }

    case 'pttTeams': {
      let updated_rank = '';
      const proTeamPtsById = new Map();

      const teams = items?.team?.Team;
      if (Array.isArray(teams) && teams.length > 0) {
        for (const t of teams) {
          if (t.teamPts != null && String(t.teamPts).trim() !== '0') {
            proTeamPtsById.set(t.id, t.teamPts);
          }
        }

        if (items?.checkpoint_id) {
          chkp_label = jQuery(`.checkpoint-item[data-checkpoint-id="${items.checkpoint_id}"]`)
            .find('h4')
            .html();
          out.push(`<h3>${chkp_label}</h3>`);
        }

        out.push(`<table class="live-center-table ptt-type-table" id="teamTable">`);
        out.push('<thead><tr>');
        out.push('<th>Rank</th><th>Team</th><th>Women</th><th>Men</th><th>Total</th>');
        if (proTeamPtsById.size > 0) out.push('<th>Team points</th>');
        out.push('</tr></thead><tbody>');

        for (const t of teams) {
          updated_rank = t.rank ? t.rank : updated_rank;

          const team_link = getPostURL('scproteams', t, items);

          out.push('<tr>');
          out.push(`<td>${escHtml(updated_rank)}</td>`);
          out.push(
            `<td>${
              t.abb
                ? 
                (team_link ? `<a class="team" href="${escHtml(team_link)}">${escHtml(t.name.trim().toUpperCase())}</a><a class="teamAbb" href="${escHtml(team_link)}">${escHtml(t.abb)}</a>` : `<span class="team">${escHtml(t.name.trim().toUpperCase())}</span><span class="teamAbb">${escHtml(t.abb)}</span>`) 
                : ''
            }</td>`
          );
          out.push(`<td>${escHtml(t.womenTime)}</td>`);
          out.push(`<td>${escHtml(t.menTime)}</td>`);
          out.push(
            `<td class="totaltime">${
              t.timeDiff ? `+ ${escHtml(t.timeDiff)}` : escHtml(t.totalTime)
            }</td>`
          );

          if (proTeamPtsById.size > 0) {
            out.push(`<td class="team_points">${t.id && proTeamPtsById.has(t.id) ? escHtml(proTeamPtsById.get(t.id)) : ''}</td>`);
          }

          out.push('</tr>');
        }

        out.push('</tbody></table>');
      }

      break;
    }

    default:
      break;
  }

  return out.join('');
}


function getResultsHtml(items){
    
    let htmlString = '';

    let startType = jQuery("#scStartType").val();

    switch (startType) {
        case 'itt':
        case 'mass':
            htmlString += getTable('normal', items);  // INDIVIDUAL TABLE
            break;

        case 'team':
        case 'teamtempo':       
            htmlString += getTableLinks(startType, items);  // INDIVIDUAL TABLE
            htmlString += getTable('pttTeams', items); // TEAM TABLE FIRST
            htmlString += getTable('normal', items); // INDIVIDUAL TABLE
            break;

        case 'sprint':
            
            htmlString += getTableLinks(startType, items);  // INDIVIDUAL TABLE
            htmlString += getTable('sprintHeatTables', items);
            htmlString += getTable('normal', items);  // INDIVIDUAL TABLE
            break;

        default:
            break;
    }

    htmlString += '<a href="#top" class="top">Back to top</a>';
    return htmlString;

}


function isInLocalStorageArray(key, value) {
    let existingArray = JSON.parse(localStorage.getItem(key)) || [];
    return existingArray.includes(value);
}


function prevH3SameLevel(el) {
  let sib = el.previousElementSibling;
  while (sib) {
    if (sib.tagName === 'H3') return sib;
    sib = sib.previousElementSibling;
  }
  return null;
}



// Function to show flag based on alpha-3 code
function displayFlag(alpha3) {

    // Map of ISO 3166-1 alpha-3 to alpha-2
    const countryMapping = {
        'ALA': 'ax',
        'AFG': 'af',
        'ALB': 'al',
        'DZA': 'dz',
        'AND': 'ad',
        'AGO': 'ao',
        'ATG': 'ag',
        'ARG': 'ar',
        'ARM': 'am',
        'AUS': 'au',
        'AUT': 'at',
        'AZE': 'az',
        'BHS': 'bs',
        'BHR': 'bh',
        'BGD': 'bd',
        'BRB': 'bb',
        'BLR': 'by',
        'BEL': 'be',
        'BLZ': 'bz',
        'BEN': 'bj',
        'BMU': 'bm',
        'BOL': 'bo',
        'BIH': 'ba',
        'BWA': 'bw',
        'BUL': 'bg',
        'BFA': 'bf',
        'BDI': 'bi',
        'KHM': 'kh',
        'CMR': 'cm',
        'CAN': 'ca',
        'CPV': 'cv',
        'CYM': 'ky',
        'CZE': 'cz',
        'CAF': 'cf',
        'TCD': 'td',
        'CHL': 'cl',
        'CHI': 'cn',
        'CHN': 'cn',
        'COL': 'co',
        'COM': 'km',
        'COG': 'cg',
        'COK': 'ck',
        'CRI': 'cr',
        'CRO': 'hr',
        'CUB': 'cu',
        'CUW': 'cw',
        'CYP': 'cy',
        'CZE': 'cz',
        'COD': 'cd',
        'DEN': 'dk',
        'GER': 'de',
        'GRL': 'gl',  // Greenland
        'DJI': 'dj',
        'DMA': 'dm',
        'DOM': 'do',
        'ECU': 'ec',
        'EGY': 'eg',
        'SLV': 'sv',
        'GNQ': 'gq',
        'ERI': 'er',
        'EST': 'ee',
        'ETH': 'et',
        'FJI': 'fj',
        'FIN': 'fi',
        'FRA': 'fr',
        'GAB': 'ga',
        'GMB': 'gm',
        'GEO': 'ge',
        'GHA': 'gh',
        'GRC': 'gr',
        'GRD': 'gd',
        'GUM': 'gu',
        'GTM': 'gt',
        'GIN': 'gn',
        'GNB': 'gw',
        'GUY': 'gy',
        'GHA': 'gh',
        'HND': 'hn',
        'HRV': 'hr',
        'HTI': 'ht',
        'HUN': 'hu',
        'ISL': 'is',
        'IND': 'in',
        'IDN': 'id',
        'IRN': 'ir',
        'IRQ': 'iq',
        'IRL': 'ie',
        'ISR': 'il',
        'ITA': 'it',
        'JAM': 'jm',
        'JOR': 'jo',
        'JPN': 'jp',
        'KAZ': 'kz',
        'KEN': 'ke',
        'KIR': 'ki',
        'KOR': 'kr',
        'KWT': 'kw',
        'KGZ': 'kg',
        'LAO': 'la',
        'LAT': 'lv',
        'LBN': 'lb',
        'LSO': 'ls',
        'LBR': 'lr',
        'LBY': 'ly',
        'LIE': 'li',
        'LTU': 'lt',
        'LUX': 'lu',
        'MDG': 'mg',
        'MWI': 'mw',
        'MYS': 'my',
        'MDV': 'mv',
        'MLI': 'ml',
        'MLT': 'mt',
        'MHL': 'mh',
        'MEX': 'mx',
        'FSM': 'fm',
        'MDA': 'md',
        'MNG': 'mn',
        'CIV': 'ci',
        'MNE': 'me',
        'MOZ': 'mz',
        'MMR': 'mm',
        'NAM': 'na',
        'NRU': 'nr',
        'NPL': 'np',
        'NED': 'nl',
        'NZL': 'nz',
        'NIC': 'ni',
        'NER': 'ne',
        'NGA': 'ng',
        'MKD': 'mk',
        'NOR': 'no',
        'OMN': 'om',
        'PAK': 'pk',
        'PLW': 'pw',
        'PAN': 'pa',
        'PNG': 'pg',
        'PRY': 'py',
        'PER': 'pe',
        'PHL': 'ph',
        'POL': 'pl',
        'BRA': 'br',
        'MAR': 'ma',
        'POR': 'pt',
        'QAT': 'qa',
        'ROU': 'ro',
        'RUS': 'ru',
        'RWA': 'rw',
        'REU': 're',
        'BLM': 'bl',
        'SHN': 'sh',
        'KNA': 'kn',
        'LCA': 'lc',
        'MAF': 'mf',
        'SPM': 'pm',
        'VCT': 'vc',
        'WSM': 'ws',
        'SMR': 'sm',
        'STP': 'st',
        'SAU': 'sa',
        'SEN': 'sn',
        'SRB': 'rs',
        'SYC': 'sc',
        'SLE': 'sl',
        'SGP': 'sg',
        'SVK': 'sk',
        'SLO': 'si',
        'SLB': 'sb',
        'SOM': 'so',
        'RSA': 'za',
        'SSD': 'ss',
        'ESP': 'es',
        'LKA': 'lk',
        'SDN': 'sd',
        'SUR': 'sr',
        'SWE': 'se',
        'SUI': 'ch',
        'SYR': 'sy',
        'TJK': 'tj',
        'TZA': 'tz',
        'THA': 'th',
        'TGO': 'tg',
        'TTO': 'tt',
        'TUN': 'tn',
        'TUR': 'tr',
        'TKM': 'tm',
        'TUV': 'tv',
        'UGA': 'ug',
        'UKR': 'ua',
        'ARE': 'ae',
        'GBR': 'gb',
        'USA': 'us',
        'URU': 'uy',
        'UZB': 'uz',
        'VUT': 'vu',
        'VEN': 've',
        'VIE': 'vn',
        'VIR': 'vi',
        'WLF': 'wf',
        'ESH': 'eh',
        'YEM': 'ye',
        'ZMB': 'zm',
        'ZWE': 'zw'
    };
    


    const countryCode = countryMapping[alpha3];
    if (countryCode) {
        return `flag-icon flag-icon-${countryCode}`;
    } else {
        // console.log('Flag not found for', alpha3);
    }
}



// --- Live Center module ---
(function() {

    const optionName = 'LiveCenterLiveUpdates';
    const countdownSeconds = 15;

    let liveUpdate = false;
    let timerInterval = null;
    let remaining = countdownSeconds;

      let toggleBtn, countdownEl, resultsEl;

    function getStoredLiveState() {
        if (sessionStorage.getItem(optionName) !== null)
          return sessionStorage.getItem(optionName) === 'true';
        if (localStorage.getItem(optionName) !== null)
            return localStorage.getItem(optionName) === 'true';
        return false;
    }

    function storeLiveState(state) {
        sessionStorage.setItem(optionName, state);
        localStorage.setItem(optionName, state);
    }

    function updateCountdownDisplay() {
        countdownEl.textContent = remaining + 's';
    }

    function fetchNewData() {

        const $targets = $(document).find('.load-container div[data-checkpoint-active="true"]');

        if ($targets.length > 0) {
            $targets.trigger("click");
        } else {
            console.warn('fetchNewData: No active checkpoint found');
        }
    } 

    function startLiveUpdates() {
        if (liveUpdate) return;
        liveUpdate = true;
        storeLiveState(true);
        remaining = countdownSeconds;
        updateCountdownDisplay();
        fetchNewData(); // fetch immediately

        timerInterval = setInterval(() => {
            remaining--;
            if (remaining <= 0) {
                remaining = countdownSeconds;
                fetchNewData();
            }
            updateCountdownDisplay();
        }, 1000);
    }

    function stopLiveUpdates() {
        liveUpdate = false;
        storeLiveState(false);
        clearInterval(timerInterval);
        countdownEl.textContent = 'Paused';
    }

    function resetLiveTimer(fetchNow = false) {
        if (!liveUpdate) return;
        remaining = countdownSeconds;
        updateCountdownDisplay();
    }

    function init() {
        toggleBtn = document.querySelector('#toggleLive');
        countdownEl = document.querySelector('#countdown');
        resultsEl = document.querySelector('#liveUpdates');

        if (!toggleBtn || !countdownEl || !resultsEl) {
            console.warn('Live Center elements not found.');
            return;
        }

        // Set initial display
        countdownEl.textContent = 'Paused';
        toggleBtn.textContent = 'Start Live';

        // Click listener
        toggleBtn.addEventListener('click', () => {
            if (liveUpdate) {
                stopLiveUpdates();
                toggleBtn.textContent = 'Start Live';
            } else {
                startLiveUpdates();
                toggleBtn.textContent = 'Stop Live';
            }
        });

        // **Start immediately if stored option is true**
        if (getStoredLiveState()) {
            startLiveUpdates();
            toggleBtn.textContent = 'Stop Live';
        }
    }

    window.initLiveCenter = init;
    window.resetLiveTimer = resetLiveTimer;
    window.startLiveUpdates = startLiveUpdates;
    window.stopLiveUpdates = stopLiveUpdates;
})();
