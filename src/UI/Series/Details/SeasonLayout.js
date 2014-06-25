﻿'use strict';
define(
    [
        'vent',
        'marionette',
        'backgrid',
        'Cells/EpisodeMonitoredCell',
        'Cells/EpisodeTitleCell',
        'Cells/RelativeDateCell',
        'Cells/EpisodeStatusCell',
        'Cells/EpisodeActionsCell',
        'Series/Details/EpisodeNumberCell',
        'Commands/CommandController',
        'moment',
        'underscore',
        'Shared/Messenger'
    ], function (vent,
                Marionette,
                Backgrid,
                ToggleCell,
                EpisodeTitleCell,
                RelativeDateCell,
                EpisodeStatusCell,
                EpisodeActionsCell,
                EpisodeNumberCell,
                CommandController,
                Moment,
                _,
                Messenger) {
        return Marionette.Layout.extend({
            template: 'Series/Details/SeasonLayoutTemplate',

            ui: {
                seasonSearch   : '.x-season-search',
                seasonMonitored: '.x-season-monitored',
                seasonRename   : '.x-season-rename'
            },

            events: {
                'click .x-season-monitored'  : '_seasonMonitored',
                'click .x-season-search'     : '_seasonSearch',
                'click .x-season-rename'     : '_seasonRename',
                'click .x-show-hide-episodes': '_showHideEpisodes',
                'dblclick .series-season h2' : '_showHideEpisodes'
            },

            regions: {
                episodeGrid: '.x-episode-grid'
            },

            columns:
                [
                    {
                        name      : 'monitored',
                        label     : '',
                        cell      : ToggleCell,
                        trueClass : 'icon-bookmark',
                        falseClass: 'icon-bookmark-empty',
                        tooltip   : 'Toggle monitored status',
                        sortable  : false
                    },
                    {
                        name : 'this',
                        label: '#',
                        cell : EpisodeNumberCell
                    },
                    {
                        name          : 'this',
                        label         : 'Title',
                        hideSeriesLink: true,
                        cell          : EpisodeTitleCell,
                        sortable      : false
                    },
                    {
                        name : 'airDateUtc',
                        label: 'Air Date',
                        cell : RelativeDateCell
                    } ,
                    {
                        name    : 'status',
                        label   : 'Status',
                        cell    : EpisodeStatusCell,
                        sortable: false
                    },
                    {
                        name    : 'this',
                        label   : '',
                        cell    : EpisodeActionsCell,
                        sortable: false
                    }
                ],

            initialize: function (options) {

                if (!options.episodeCollection) {
                    throw 'episodeCollection is needed';
                }

                this.episodeCollection = options.episodeCollection.bySeason(this.model.get('seasonNumber'));

                var self = this;

                this.episodeCollection.each(function (model) {
                    model.episodeCollection = self.episodeCollection;
                });

                this.series = options.series;

                this.showingEpisodes = this._shouldShowEpisodes();

                this.listenTo(this.model, 'sync', this._afterSeasonMonitored);
                this.listenTo(this.episodeCollection, 'sync', this.render);
            },

            onRender: function () {

                if (this.showingEpisodes) {
                    this._showEpisodes();
                }

                this._setSeasonMonitoredState();

                CommandController.bindToCommand({
                    element: this.ui.seasonSearch,
                    command: {
                        name        : 'seasonSearch',
                        seriesId    : this.series.id,
                        seasonNumber: this.model.get('seasonNumber')
                    }
                });

                CommandController.bindToCommand({
                    element: this.ui.seasonRename,
                    command: {
                        name        : 'renameFiles',
                        seriesId    : this.series.id,
                        seasonNumber: this.model.get('seasonNumber')
                    }
                });
            },

            _seasonSearch: function () {

                CommandController.Execute('seasonSearch', {
                    name        : 'seasonSearch',
                    seriesId    : this.series.id,
                    seasonNumber: this.model.get('seasonNumber')
                });
            },

            _seasonRename: function () {
                vent.trigger(vent.Commands.ShowRenamePreview, { series: this.series, seasonNumber: this.model.get('seasonNumber') });
            },

            _seasonMonitored: function () {
                if (!this.series.get('monitored')) {

                    Messenger.show({
                        message: 'Unable to change monitored state when series is not monitored',
                        type   : 'error'
                    });

                    return;
                }

                var name = 'monitored';
                this.model.set(name, !this.model.get(name));
                this.series.setSeasonMonitored(this.model.get('seasonNumber'));

                var savePromise = this.series.save().always(this._afterSeasonMonitored.bind(this));

                this.ui.seasonMonitored.spinForPromise(savePromise);
            },

            _afterSeasonMonitored: function () {
                var self = this;

                _.each(this.episodeCollection.models, function (episode) {
                    episode.set({ monitored: self.model.get('monitored') });
                });

                this.render();
            },

            _setSeasonMonitoredState: function () {
                this.ui.seasonMonitored.removeClass('icon-spinner icon-spin');

                if (this.model.get('monitored')) {
                    this.ui.seasonMonitored.addClass('icon-bookmark');
                    this.ui.seasonMonitored.removeClass('icon-bookmark-empty');
                }
                else {
                    this.ui.seasonMonitored.addClass('icon-bookmark-empty');
                    this.ui.seasonMonitored.removeClass('icon-bookmark');
                }
            },

            _showEpisodes: function () {
                this.episodeGrid.show(new Backgrid.Grid({
                    columns   : this.columns,
                    collection: this.episodeCollection,
                    className : 'table table-hover season-grid'
                }));
            },

            _shouldShowEpisodes: function () {
                var startDate = Moment().add('month', -1);
                var endDate = Moment().add('year', 1);

                return this.episodeCollection.some(function (episode) {

                    var airDate = episode.get('airDateUtc');

                    if (airDate) {
                        var airDateMoment = Moment(airDate);

                        if (airDateMoment.isAfter(startDate) && airDateMoment.isBefore(endDate)) {
                            return true;
                        }
                    }

                    return false;
                });
            },

            templateHelpers: function () {

                var episodeCount = this.episodeCollection.filter(function (episode) {
                    return episode.get('hasFile') || (episode.get('monitored') && Moment(episode.get('airDateUtc')).isBefore(Moment()));
                }).length;

                var episodeFileCount = this.episodeCollection.where({ hasFile: true }).length;
                var percentOfEpisodes = 100;

                if (episodeCount > 0) {
                    percentOfEpisodes = episodeFileCount / episodeCount * 100;
                }

                return {
                    showingEpisodes  : this.showingEpisodes,
                    episodeCount     : episodeCount,
                    episodeFileCount : episodeFileCount,
                    percentOfEpisodes: percentOfEpisodes
                };
            },

            _showHideEpisodes: function () {
                if (this.showingEpisodes) {
                    this.showingEpisodes = false;
                    this.episodeGrid.close();
                }
                else {
                    this.showingEpisodes = true;
                    this._showEpisodes();
                }

                this.templateHelpers.showingEpisodes = this.showingEpisodes;
                this.render();
            },

            _episodeMonitoredToggled: function (options) {
                var model = options.model;
                var shiftKey = options.shiftKey;

                if (!this.episodeCollection.get(model.get('id'))) {
                    return;
                }

                if (!shiftKey) {
                    return;
                }

                var lastToggled = this.episodeCollection.lastToggled;

                if (!lastToggled) {
                    return;
                }

                var currentIndex = this.episodeCollection.indexOf(model);
                var lastIndex = this.episodeCollection.indexOf(lastToggled);

                var low = Math.min(currentIndex, lastIndex);
                var high = Math.max(currentIndex, lastIndex);
                var range = _.range(low + 1, high);

                this.episodeCollection.lastToggled = model;
            }
        });
    });
